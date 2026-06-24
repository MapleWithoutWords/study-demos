using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AIVectorMemeoryStoreConsole3
{
    internal class QueryVectorItemDto
    {
        public VectorModel Vector { get; set; } = new();

        public float Score { get; set; }
    }
}
