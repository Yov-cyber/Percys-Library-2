using System.Text.Json.Serialization;

namespace ComicReader.Models
{
    public class ContinueStorage
    {
        [JsonPropertyName("items")]
        public ContinueItem[] Items { get; set; } = new ContinueItem[0];

        [JsonPropertyName("completedItems")]
        public ContinueItem[] CompletedItems { get; set; } = new ContinueItem[0];
    }
}
