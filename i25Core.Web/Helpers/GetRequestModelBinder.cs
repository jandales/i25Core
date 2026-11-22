using i25Core.BusinessLayer.Parameters;
using i25Core.BusinessLayer;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.Text.Json;

namespace i25Core.Web.Helpers
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

            bindingContext.Result = ModelBindingResult.Success(request);

            return Task.CompletedTask;
        }
    }
}
