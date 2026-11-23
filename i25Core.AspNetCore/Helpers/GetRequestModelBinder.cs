using i25Core.BusinessLayer.Parameters;
using i25Core.BusinessLayer;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.Text.Json;

namespace i25Core.AspNetCore.Helpers
{
 

    public class GetRequestModelBinder : IModelBinder
    {
        public Task BindModelAsync(ModelBindingContext bindingContext)
        {          

            var filters = bindingContext.ValueProvider
                .GetValue("Filters").FirstValue;

            var sorts = bindingContext.ValueProvider
                .GetValue("Sorts").FirstValue;

            var request = new GetRequest();
          

            if (!string.IsNullOrWhiteSpace(filters))
            {
                request.Filters = JsonSerializer.Deserialize<List<GetFilter>>(
                    filters,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }) ?? new List<GetFilter>();
            }

            if (!string.IsNullOrWhiteSpace(sorts))
            {
                request.Sorts = JsonSerializer.Deserialize<List<GetSort>>(
                    sorts,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }) ?? new List<GetSort>();
            }

            request.Page = ReadPositiveInt(bindingContext, "Page");
            request.PageSize = ReadPositiveInt(bindingContext, "PageSize");

            bindingContext.Result = ModelBindingResult.Success(request);

            return Task.CompletedTask;
        }

        // Invalid values surface as 400 through GlobalExceptionHandler (ArgumentException).
        private static int? ReadPositiveInt(ModelBindingContext bindingContext, string name)
        {
            var raw = bindingContext.ValueProvider.GetValue(name).FirstValue;

            if (string.IsNullOrWhiteSpace(raw))
                return null;

            if (!int.TryParse(raw, out var value) || value < 1)
                throw new ArgumentException($"{name} must be a whole number greater than 0.");

            return value;
        }
    }
}
