using System.Collections.Generic;
using System.Threading.Tasks;
using Kiriha.Models.Entities;

namespace Kiriha.Core.Services;

public interface IMappingService
{
    Task<int?> GetIdFromTitleAsync(string title, IEnumerable<AnimeEntity> userList);
    Task<int?> SearchOnMalAsync(string title);
    
    void AddMapping(string title, int animeId);
    void RemoveMapping(string title);
    void AddNegativeMapping(string title);
    
    bool IsManuallyMapped(string title);
    bool IsNegativelyMapped(string title);
}
