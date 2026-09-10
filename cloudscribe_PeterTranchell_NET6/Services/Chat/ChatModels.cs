using System.Collections.Generic;

namespace cloudscribe_PeterTranchell_NET6.Services.Chat
{
    public class PageDocument
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string MetaDescription { get; set; } = string.Empty;
        public string Thumbnail { get; set; } = string.Empty;
    }

    public class CorpusPage
    {
        public string Url { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Hash { get; set; } = string.Empty;
        public List<string> ChunkTexts { get; set; } = new List<string>();
    }

    public class CorpusSnapshot
    {
        public string Version { get; set; } = string.Empty;
        public List<CorpusPage> Pages { get; set; } = new List<CorpusPage>();
    }

    public class ChatMessageDto
    {
        public string Role { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }

    public class ChatRequestDto
    {
        public List<ChatMessageDto> Messages { get; set; } = new List<ChatMessageDto>();
        public string CaptchaToken { get; set; } = string.Empty;
        public string Nonce { get; set; } = string.Empty;
    }

    public class ChatSourceDto
    {
        public string Title { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
    }

    public class ChatResponseDto
    {
        public string Reply { get; set; } = string.Empty;
        public List<ChatSourceDto> Sources { get; set; } = new List<ChatSourceDto>();
    }

    public class RetrievedChunk
    {
        public string Url { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public double Score { get; set; }
    }

    public class RetrievalResult
    {
        public List<RetrievedChunk> Chunks { get; set; } = new List<RetrievedChunk>();
        public List<ChatSourceDto> Sources { get; set; } = new List<ChatSourceDto>();
    }
}
