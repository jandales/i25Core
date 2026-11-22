using FluentValidation;
using i25Core.Web.Validator.Base;
using i25Core.Core.Entities;

namespace i25Core.Web.Validator
{
    public class CategoryValidator : BaseValidator<Category>
    {
        public CategoryValidator()
        {
            RuleFor(x => x.CategoryCode)
                .NotEmpty()
                .WithMessage("Category code is required.");

            RuleFor(x => x.CategoryName)
                .NotEmpty()
                .WithMessage("Category name is required.")
                .MaximumLength(150);
        }
    }
}
