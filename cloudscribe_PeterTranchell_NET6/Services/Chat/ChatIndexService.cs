using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace cloudscribe_PeterTranchell_NET6.Services.Chat
{
    public class ChatIndexService
    {
        private const string MetaFile = "chat-cache-meta.json";
        private const string VectorFile = "chat-cache-vectors.bin";

        private readonly SiteCorpusProvider _corpusProvider;
        private readonly EmbeddingClient _embeddings;
        private readonly IWebHostEnvironment _env;
        private readonly ChatOptions _options;
        private readonly ILogger<ChatIndexService> _logger;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        private volatile IndexSnapshot _snapshot = new IndexSnapshot();
        private IndexSnapshot _docFreqSnapshot;
        private Dictionary<string, int> _docFreq;
        private bool _built;

        public ChatIndexService(
            SiteCorpusProvider corpusProvider,
            EmbeddingClient embeddings,
            IWebHostEnvironment env,
            IOptions<ChatOptions> optionsAccessor,
            ILogger<ChatIndexService> logger)
        {
            _corpusProvider = corpusProvider;
            _embeddings = embeddings;
            _env = env;
            _options = optionsAccessor.Value;
            _logger = logger;
        }

        public bool HasVectors => _snapshot.Chunks.Any(c => c.Vector.Length > 0);

        public bool HasBuilt => _built;

        public bool EmbeddingsConfigured => _embeddings.IsConfigured;

        public async Task<RetrievalResult> RetrieveAsync(string query, int topK)
        {
            if (!_built)
            {
                await EnsureBuiltAsync().ConfigureAwait(false);
            }

            var snapshot = _snapshot;
            var result = new RetrievalResult();
            if (snapshot.Chunks.Count == 0 || string.IsNullOrWhiteSpace(query)) return result;

            float[] queryVector = null;
            var queryNorm = 0f;
            if (_embeddings.IsConfigured && HasVectors)
            {
                queryVector = await _embeddings.EmbedAsync(query).ConfigureAwait(false);
                if (queryVector != null) queryNorm = Norm(queryVector);
                else _logger.LogWarning("Query embedding failed; falling back to keyword scoring");
            }
            var useVectors = queryVector != null;

            var queryTerms = Tokenize(query);

            var docFreq = GetDocFreq(snapshot);
            var chunkCount = Math.Max(1, snapshot.Chunks.Count);
            var idf = new Dictionary<string, double>(StringComparer.Ordinal);
            double idfSum = 0;
            foreach (var term in queryTerms.Distinct(StringComparer.Ordinal))
            {
                docFreq.TryGetValue(term, out var df);
                var weight = Math.Log(1.0 + (double)chunkCount / (1 + df));
                idf[term] = weight;
                idfSum += weight;
            }

            var scored = new List<(IndexedChunk Chunk, double Score)>(snapshot.Chunks.Count);
            foreach (var chunk in snapshot.Chunks)
            {
                var keyword = KeywordScore(chunk, idf, idfSum);

                double score;
                if (useVectors && chunk.Vector.Length > 0)
                {
                    var dot = DotProduct(queryVector!, chunk.Vector);
                    var cosine = dot / (queryNorm * chunk.Norm);
                    if (cosine < 0) cosine = 0;
                    score = 0.75 * cosine + 0.25 * keyword;
                }
                else
                {
                    score = keyword;
                }
                scored.Add((chunk, score));
            }

            var top = scored
                .OrderByDescending(x => x.Score)
                .Take(topK * 4)
                .ToList();

            var perDoc = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var picked = new List<RetrievedChunk>();
            var sources = new List<ChatSourceDto>();
            var seenDocs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in top)
            {
                if (picked.Count >= topK) break;
                perDoc.TryGetValue(item.Chunk.Url, out var count);
                if (count >= 3) continue;
                perDoc[item.Chunk.Url] = count + 1;
                picked.Add(new RetrievedChunk
                {
                    Url = item.Chunk.Url,
                    Title = item.Chunk.Title,
                    Text = item.Chunk.Text,
                    Score = Math.Round(item.Score, 4)
                });
                if (seenDocs.Add(item.Chunk.Url))
                {
                    sources.Add(new ChatSourceDto { Title = item.Chunk.Title, Url = item.Chunk.Url });
                }
            }

            result.Chunks = picked;
            result.Sources = sources.Take(8).ToList();
            return result;
        }

        public async Task EnsureBuiltAsync(bool forceRebuild = false)
        {
            await _lock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_built && !forceRebuild) return;

                var corpus = await _corpusProvider.TryLoadAsync().ConfigureAwait(false);
                if (corpus == null) return;

                var reuseFrom = ResolveReuseFrom(forceRebuild);
                if (reuseFrom != null && string.Equals(reuseFrom.Version, corpus.Version, StringComparison.Ordinal))
                {
                    var cachedChunks = reuseFrom.Docs.Sum(d => d.Vectors.Count);
                    if (cachedChunks == corpus.Pages.Sum(p => p.ChunkTexts.Count))
                    {
                        _snapshot = await BuildSnapshotAsync(corpus, reuseFrom: reuseFrom).ConfigureAwait(false);
                        if (!HasVectors && _embeddings.IsConfigured)
                        {
                            _snapshot = await BuildSnapshotAsync(corpus, reuseFrom: null).ConfigureAwait(false);
                        }
                        _built = true;
                        LogIndexState();
                        return;
                    }
                }

                _snapshot = await BuildSnapshotAsync(corpus, reuseFrom: reuseFrom).ConfigureAwait(false);
                _built = true;
                LogIndexState();
            }
            finally
            {
                _lock.Release();
            }
        }

        private IndexSnapshot ResolveReuseFrom(bool forceRebuild)
        {
            if (forceRebuild) return null;

            if (_snapshot.Docs.Count > 0) return _snapshot;

            var cached = LoadCachedSnapshot();
            if (cached == null) return null;

            if (IsCacheStillFresh()) return cached;

            _logger.LogInformation("Chat cache expired; will re-embed");
            return null;
        }

        private void LogIndexState()
        {
            var total = _snapshot.Chunks.Count;
            var vectorized = _snapshot.Chunks.Count(c => c.Vector.Length > 0);
            if (vectorized == 0 && _embeddings.IsConfigured)
            {
                _logger.LogWarning(
                    "Chat index built in KEYWORD-ONLY mode: {Chunks} chunks, 0 vectorized. Check earlier embedding errors. Cache folder: {Folder}",
                    total, CacheFolder());
            }
            else
            {
                _logger.LogInformation(
                    "Chat index ready: {Chunks} chunks ({Vectorized} vectorized), version {Version}. Cache folder: {Folder}",
                    total, vectorized, _snapshot.Version, CacheFolder());
            }
        }

        public async Task RefreshIfChangedAsync()
        {
            await _lock.WaitAsync().ConfigureAwait(false);
            try
            {
                var corpus = await _corpusProvider.TryLoadAsync().ConfigureAwait(false);
                if (corpus == null) return;
                if (string.Equals(corpus.Version, _snapshot.Version, StringComparison.Ordinal))
                {
                    return;
                }
                _logger.LogInformation(
                    "Chat index refreshing from version {Old} to {New}",
                    _snapshot.Version, corpus.Version);
                _snapshot = await BuildSnapshotAsync(corpus, reuseFrom: _snapshot).ConfigureAwait(false);
                _built = true;
            }
            finally
            {
                _lock.Release();
            }
        }

        private async Task<IndexSnapshot> BuildSnapshotAsync(CorpusSnapshot corpus, IndexSnapshot reuseFrom)
        {
            var reuse = new Dictionary<string, DocVectors>();
            if (reuseFrom != null)
            {
                foreach (var doc in reuseFrom.Docs)
                {
                    reuse[doc.Url] = doc;
                }
            }

            var snapshot = new IndexSnapshot { Version = corpus.Version };
            var pending = new List<IndexedChunk>();
            var reusedCount = 0;
            var hashByUrl = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var page in corpus.Pages)
            {
                reuse.TryGetValue(page.Url, out var cached);
                if (cached != null && cached.Hash != page.Hash) cached = null;
                hashByUrl[page.Url] = page.Hash;

                for (var i = 0; i < page.ChunkTexts.Count; i++)
                {
                    var text = page.ChunkTexts[i];
                    var chunk = new IndexedChunk
                    {
                        Url = page.Url,
                        Title = page.Title,
                        Text = text,
                        Terms = BuildTermCounts(page.Title, text)
                    };

                    if (cached != null && i < cached.Vectors.Count)
                    {
                        AssignVector(chunk, cached.Vectors[i]);
                        reusedCount++;
                    }
                    else
                    {
                        pending.Add(chunk);
                    }
                    snapshot.Chunks.Add(chunk);
                }
            }

            if (_embeddings.IsConfigured && pending.Count > 0)
            {
                _logger.LogInformation(
                    "Embedding {Pending} chat chunks ({Reused} reused from cache)",
                    pending.Count, reusedCount);
                var texts = pending.Select(c => c.Title + "\n" + c.Text).ToArray();
                var vectors = await _embeddings.EmbedBatchesAsync(texts).ConfigureAwait(false);
                for (var i = 0; i < pending.Count; i++)
                {
                    var vector = vectors[i];
                    if (vector != null) AssignVector(pending[i], vector);
                }
            }
            else if (pending.Count > 0)
            {
                _logger.LogWarning(
                    "Skipping embeddings for {Pending} chunks (embedding client not configured - check environment/settings)",
                    pending.Count);
            }

            DocVectors currentDoc = null;
            foreach (var chunk in snapshot.Chunks)
            {
                if (currentDoc == null || !string.Equals(currentDoc.Url, chunk.Url, StringComparison.Ordinal))
                {
                    currentDoc = new DocVectors { Url = chunk.Url, Hash = hashByUrl[chunk.Url] };
                    snapshot.Docs.Add(currentDoc);
                }
                currentDoc.Vectors.Add(chunk.Vector);
            }

            var nothingChanged = pending.Count == 0 && reuseFrom != null
                && string.Equals(reuseFrom.Version, corpus.Version, StringComparison.Ordinal);
            if (nothingChanged)
            {
                _logger.LogInformation(
                    "Chat cache unchanged ({Reused} chunks reused, version {Version}); skipping save",
                    reusedCount, corpus.Version);
            }
            else
            {
                SaveCache(snapshot);
            }
            return snapshot;
        }

        private void SaveCache(IndexSnapshot snapshot)
        {
            try
            {
                var folder = CacheFolder();
                Directory.CreateDirectory(folder);
                var metaPath = Path.Combine(folder, MetaFile);
                var binPath = Path.Combine(folder, VectorFile);

                var hasAny = snapshot.Docs.Any(d => d.Vectors.Any(v => v.Length > 0));
                if (!hasAny)
                {
                    _logger.LogWarning(
                        "Chat index produced no usable vectors ({Docs} docs); skipping cache write",
                        snapshot.Docs.Count);
                    if (File.Exists(metaPath)) File.Delete(metaPath);
                    if (File.Exists(binPath)) File.Delete(binPath);
                    return;
                }

                using (var stream = File.Create(binPath))
                using (var writer = new BinaryWriter(stream))
                {
                    foreach (var doc in snapshot.Docs)
                    {
                        foreach (var vector in doc.Vectors)
                        {
                            if (vector.Length == 0) continue;
                            foreach (var value in vector) writer.Write(value);
                        }
                    }
                }

                var meta = new CacheMeta { Model = _options.EmbeddingDeployment, Version = snapshot.Version, Params = ChunkParamStamp() };
                foreach (var doc in snapshot.Docs)
                {
                    var flags = new StringBuilder(doc.Vectors.Count);
                    foreach (var vector in doc.Vectors)
                    {
                        flags.Append(vector.Length > 0 ? '1' : '0');
                    }
                    meta.Docs.Add(new CachedDocMeta
                    {
                        Url = doc.Url,
                        Hash = doc.Hash,
                        Dims = doc.Vectors.FirstOrDefault(v => v.Length > 0)?.Length ?? 0,
                        Flags = flags.ToString()
                    });
                }
                File.WriteAllText(metaPath, JsonSerializer.Serialize(meta));
                _logger.LogInformation(
                    "Saved chat cache: meta {MetaBytes} bytes, vectors {VectorBytes} bytes -> {BinPath}",
                    new FileInfo(metaPath).Length, new FileInfo(binPath).Length, binPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist chat vector cache");
            }
        }

        private string CacheFolder()
        {
            return Path.Combine(_env.ContentRootPath, "App_Data");
        }

        private string ChunkParamStamp()
        {
            return _options.ChunkChars + ":" + _options.ChunkOverlapChars;
        }

        private bool IsCacheStillFresh()
        {
            var folder = CacheFolder();
            var metaPath = Path.Combine(folder, MetaFile);
            var binPath = Path.Combine(folder, VectorFile);
            if (!File.Exists(metaPath) || !File.Exists(binPath)) return false;

            var cacheTime = File.GetLastWriteTimeUtc(metaPath);
            var age = DateTime.UtcNow - cacheTime;

            if (age.TotalHours >= _options.RefreshIntervalHours)
            {
                _logger.LogInformation(
                    "Chat cache expired: {Age:F1}h old, refresh interval is {Interval}h",
                    age.TotalHours, _options.RefreshIntervalHours);
                return false;
            }

            var versionPath = Path.Combine(_env.WebRootPath!, "lunr-index", "version.txt");
            if (File.Exists(versionPath))
            {
                var versionTime = File.GetLastWriteTimeUtc(versionPath);
                if (versionTime > cacheTime)
                {
                    _logger.LogInformation(
                        "Chat cache stale: version.txt is newer than cache files");
                    return false;
                }
            }

            return true;
        }

        private IndexSnapshot LoadCachedSnapshot()
        {
            try
            {
                var folder = CacheFolder();
                var metaPath = Path.Combine(folder, MetaFile);
                var binPath = Path.Combine(folder, VectorFile);
                if (!File.Exists(metaPath) || !File.Exists(binPath)) return null;

                var meta = JsonSerializer.Deserialize<CacheMeta>(File.ReadAllText(metaPath));
                if (meta == null) return null;
                if (!string.Equals(meta.Model, _options.EmbeddingDeployment, StringComparison.Ordinal)) return null;
                if (!string.Equals(meta.Params, ChunkParamStamp(), StringComparison.Ordinal))
                {
                    _logger.LogInformation("Chat vector cache ignored: chunk parameters changed");
                    return null;
                }

                var snapshot = new IndexSnapshot { Version = meta.Version ?? string.Empty };
                using var stream = File.OpenRead(binPath);
                using var reader = new BinaryReader(stream);

                foreach (var docMeta in meta.Docs)
                {
                    var doc = new DocVectors { Url = docMeta.Url, Hash = docMeta.Hash };
                    if (docMeta.Dims <= 0 || string.IsNullOrEmpty(docMeta.Flags))
                    {
                        continue;
                    }
                    for (var i = 0; i < docMeta.Flags.Length; i++)
                    {
                        if (docMeta.Flags[i] != '1')
                        {
                            doc.Vectors.Add(Array.Empty<float>());
                            continue;
                        }
                        var vector = new float[docMeta.Dims];
                        for (var d = 0; d < docMeta.Dims; d++) vector[d] = reader.ReadSingle();
                        doc.Vectors.Add(vector);
                    }
                    snapshot.Docs.Add(doc);
                }

                _logger.LogInformation("Loaded chat vector cache: {Docs} docs, version {Version}", snapshot.Docs.Count, snapshot.Version);
                return snapshot;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load chat vector cache; will re-embed");
                return null;
            }
        }

        private static void AssignVector(IndexedChunk chunk, float[] vector)
        {
            chunk.Vector = vector;
            chunk.Norm = Norm(vector);
        }

        private static float Norm(float[] vector)
        {
            var sum = 0f;
            for (var i = 0; i < vector.Length; i++) sum += vector[i] * vector[i];
            return (float)Math.Sqrt(sum);
        }

        private static double DotProduct(float[] a, float[] b)
        {
            var len = Math.Min(a.Length, b.Length);
            var sum = 0d;
            for (var i = 0; i < len; i++) sum += (double)a[i] * b[i];
            return sum;
        }

        private static Dictionary<string, short> BuildTermCounts(string title, string text)
        {
            var counts = new Dictionary<string, short>(StringComparer.OrdinalIgnoreCase);
            foreach (var term in Tokenize(title))
            {
                counts[term] = (short)(counts.TryGetValue(term, out var c) ? c + 2 : 2);
            }
            foreach (var term in Tokenize(text))
            {
                if (counts.TryGetValue(term, out var existing))
                {
                    if (existing < short.MaxValue - 1) counts[term] = (short)(existing + 1);
                }
                else
                {
                    counts[term] = 1;
                }
            }
            return counts;
        }

        private static double KeywordScore(IndexedChunk chunk, Dictionary<string, double> idfWeights, double idfSum)
        {
            if (idfSum <= 0 || chunk.Terms.Count == 0 || idfWeights.Count == 0) return 0;
            double score = 0;
            foreach (var pair in idfWeights)
            {
                if (chunk.Terms.TryGetValue(pair.Key, out var tf)) score += Math.Min((int)tf, 3) * pair.Value;
            }
            return score / idfSum;
        }

        private Dictionary<string, int> GetDocFreq(IndexSnapshot snapshot)
        {
            if (_docFreq == null || !ReferenceEquals(_docFreqSnapshot, snapshot))
            {
                var df = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var chunk in snapshot.Chunks)
                {
                    foreach (var term in chunk.Terms.Keys)
                    {
                        df[term] = df.TryGetValue(term, out var n) ? n + 1 : 1;
                    }
                }
                _docFreq = df;
                _docFreqSnapshot = snapshot;
            }
            return _docFreq;
        }

        private static List<string> Tokenize(string input)
        {
            var results = new List<string>();
            if (string.IsNullOrEmpty(input)) return results;
            var start = -1;
            for (var i = 0; i <= input.Length; i++)
            {
                var c = i < input.Length ? char.ToLowerInvariant(input[i]) : '\0';
                var isTerm = char.IsLetterOrDigit(c) || c == '\'';
                if (isTerm && start < 0) start = i;
                if (!isTerm && start >= 0)
                {
                    var word = input.Substring(start, i - start).Trim('\'');
                    start = -1;
                    if (word.Length >= 2 && word.Length <= 30 && !Stopwords.Contains(word))
                    {
                        if (word.EndsWith("'s")) word = word.Substring(0, word.Length - 2);
                        results.Add(NormalizeTerm(word));
                    }
                }
            }
            return results;
        }

        private static string NormalizeTerm(string word)
        {
            if (word.Length > 3 && word.EndsWith("s")
                && !word.EndsWith("ss") && !word.EndsWith("us") && !word.EndsWith("is"))
            {
                return word.Substring(0, word.Length - 1);
            }
            return word;
        }

        private static readonly HashSet<string> Stopwords = new HashSet<string>(StringComparer.Ordinal)
        {
            "the","a","an","of","and","or","to","in","for","on","at","by","with","from","as","is","are","was","were",
            "be","been","it","its","this","that","these","those","he","she","his","her","they","them","their",
            "i","im","me","my","we","our","you","your","do","does","did","can","could","would","should","will",
            "what","which","who","whom","whose","where","when","why","how","any","some","there","here","about",
            "have","has","had","not","no","yes","but","if","then","than","so","also","into","up","out","all"
        };

        private class IndexedChunk
        {
            public string Url { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string Text { get; set; } = string.Empty;
            public float[] Vector { get; set; } = Array.Empty<float>();
            public float Norm { get; set; }
            public Dictionary<string, short> Terms { get; set; } = new Dictionary<string, short>();
        }

        private class DocVectors
        {
            public string Url { get; set; } = string.Empty;
            public string Hash { get; set; } = string.Empty;
            public List<float[]> Vectors { get; set; } = new List<float[]>();
        }

        private class IndexSnapshot
        {
            public string Version { get; set; } = string.Empty;
            public List<IndexedChunk> Chunks { get; set; } = new List<IndexedChunk>();
            public List<DocVectors> Docs { get; set; } = new List<DocVectors>();
        }

        private class CacheMeta
        {
            public string Model { get; set; } = string.Empty;
            public string Version { get; set; } = string.Empty;
            public string Params { get; set; } = string.Empty;
            public List<CachedDocMeta> Docs { get; set; } = new List<CachedDocMeta>();
        }

        private class CachedDocMeta
        {
            public string Url { get; set; } = string.Empty;
            public string Hash { get; set; } = string.Empty;
            public int Dims { get; set; }
            public string Flags { get; set; } = string.Empty;
        }
    }
}
