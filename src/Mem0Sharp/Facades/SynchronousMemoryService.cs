namespace Mem0Sharp;

public sealed class SynchronousMemoryService
{
    private readonly IMemoryService service;

    public SynchronousMemoryService(IMemoryService service) => this.service = service;

    public AddResult Add(string text, MemoryAddOptions? options = null) => options is null ? service.AddAsync(text).GetAwaiter().GetResult() : service.AddAsync(text, options).GetAwaiter().GetResult();
    public AddResult Add(IEnumerable<Message> messages, MemoryAddOptions? options = null) => options is null ? service.AddAsync(messages).GetAwaiter().GetResult() : service.AddAsync(messages, options).GetAwaiter().GetResult();
    public AddResult AddMany(IEnumerable<string> texts, MemoryAddOptions? options = null) => service.AddManyAsync(texts, options).GetAwaiter().GetResult();
    public IReadOnlyList<SearchResult> Search(string query, MemorySearchOptions? options = null) => options is null ? service.SearchAsync(query).GetAwaiter().GetResult() : service.SearchAsync(query, options).GetAwaiter().GetResult();
    public Memory? Get(string id) => service.GetAsync(id).GetAwaiter().GetResult();
    public IReadOnlyList<Memory> GetAll(MemoryFilter? filter = null) => service.GetAllAsync(filter).GetAwaiter().GetResult();
    public Memory Update(string id, MemoryUpdate update) => service.UpdateAsync(id, update).GetAwaiter().GetResult();
    public void Delete(string id) => service.DeleteAsync(id).GetAwaiter().GetResult();
    public int DeleteAll(MemoryFilter? filter = null) => service.DeleteAllAsync(filter).GetAwaiter().GetResult();
    public IReadOnlyList<MemoryHistoryEntry> History(string id) => service.GetHistoryAsync(id).GetAwaiter().GetResult();
    public void Reset() => service.ResetAsync().GetAwaiter().GetResult();
}