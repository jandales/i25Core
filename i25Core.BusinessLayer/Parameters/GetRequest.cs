using i25Core.BusinessLayer.Parameters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace i25Core.BusinessLayer
{
    public class GetRequest
    { 
        public List<GetFilter> Filters { get; set; } = new();
        public List<GetSort> Sorts { get; set; } = new();

    }
}
