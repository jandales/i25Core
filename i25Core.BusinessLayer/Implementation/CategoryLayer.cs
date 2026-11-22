using i25Core.BusinessLayer.Implementation.Base;
using i25Core.BusinessLayer.Implementation.Interface;
using i25Core.Infrastructure;
using i25Core.Core.Entities;

namespace i25Core.BusinessLayer.Implementation
{
    public class CategoryLayer : BaseBusinessLayer<Category>
    {
        private IRepository _db;
        public CategoryLayer(IRepository db) : base(db)
        {
            _db= db;
        }
    }
}
