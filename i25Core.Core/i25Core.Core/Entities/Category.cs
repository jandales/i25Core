using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace i25Core.Core.Entities
{
    public class Category : BaseEntity
    {       
        public string? CategoryCode { get; set; }    
        public string? CategoryName { get; set; } 
        public string? Description { get; set; }
        
    }

}
