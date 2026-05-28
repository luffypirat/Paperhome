using System.Collections.Generic;

namespace Paperhome.Models
{
    public class TagRecord
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        
        // Связь с документами (Многие-ко-многим)
        public List<DocumentRecord> Documents { get; set; } = new();
    }
}
