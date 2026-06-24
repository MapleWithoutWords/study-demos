using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AIVectorMemeoryStoreConsole3;

public class VectorModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Data { get; set; } = string.Empty;

    public float[] Embedding { get; set; } = Array.Empty<float>();

    public Dictionary<string, object> Metadata { get; set; } = new();

    public string? Hash { get; set; }

    public string? UserId { get; set; }

    /// <summary>
    /// 用哪个Agent算出来的向量值
    /// </summary>
    public string? AgentId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}
