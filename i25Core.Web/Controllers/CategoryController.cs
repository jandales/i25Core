using i25Core.BusinessLayer.Implementation;
using i25Core.Core.Entities;

namespace i25Core.Web.Controllers
{
    public class CategoryController : BaseController<Category>
    {
        public CategoryController(IBusinessLayer<Category> service) : base(service){}
    }
}
