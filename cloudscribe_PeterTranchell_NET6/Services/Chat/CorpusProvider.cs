using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace cloudscribe_PeterTranchell_NET6.Services.Chat
{
    public class CorpusProvider : ICorpusProvider
    {
        private readonly DatabaseCorpusProvider _database;
        private readonly SiteCorpusProvider _lunr;
        private readonly ChatOptions _options;
        private readonly ILogger<CorpusProvider> _logger;

        public CorpusProvider(
            DatabaseCorpusProvider database,
            SiteCorpusProvider lunr,
            IOptions<ChatOptions> optionsAccessor,
            ILogger<CorpusProvider> logger)
        {
            _database = database;
            _lunr = lunr;
            _options = optionsAccessor.Value;
            _logger = logger;
        }

        public CorpusSource Source => _options.Source;

        public Task<CorpusSnapshot> TryLoadAsync()
        {
            return _options.Source == CorpusSource.Database
                ? _database.TryLoadAsync()
                : _lunr.TryLoadAsync();
        }
    }
}
