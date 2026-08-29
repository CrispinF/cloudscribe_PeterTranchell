using System.Threading.Tasks;

namespace cloudscribe_PeterTranchell_NET6.Services.Chat
{
    public interface ICorpusProvider
    {
        Task<CorpusSnapshot> TryLoadAsync();
    }
}
