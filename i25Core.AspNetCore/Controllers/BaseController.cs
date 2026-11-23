using FluentValidation;
using i25Core.AspNetCore.Helpers;
using i25Core.BusinessLayer;
using i25Core.BusinessLayer.Implementation;
using i25Core.Core.DTO;
using i25Core.Core.Localization;
using i25Core.AspNetCore.Caching;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using System.Text.Json;


namespace i25Core.AspNetCore.Controllers
{
    [ApiController]
    // Two URLs for every controller: /api/Vendor (the default version, kept so existing clients work)
    // and /api/v1/Vendor (explicit version). A controller with no [ApiVersion] is version 1.0.
    [Route("api/[controller]")]
    [Route("api/v{version:apiVersion}/[controller]")]
    public abstract class BaseController<T> : ControllerBase
        where T : class
    {
        protected readonly IBusinessLayer<T> _service;

        public BaseController(IBusinessLayer<T> service)
        {
            _service = service;
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] T entity)
        {
            var validation = await ValidateModelAsync(entity);

            if (validation != null)
                return validation;

            await _service.AddAsync(entity);
            await EvictResponseCacheAsync();

            return OkResponse(entity, Translate("messages.created"));
        }

        [HttpPut]
        public async Task<IActionResult> Update([FromBody] T entity)
        {
            var validation = await ValidateModelAsync(entity);

            if (validation != null)
                return validation;

            await _service.UpdateSync(entity);
            await EvictResponseCacheAsync();

            return OkResponse(entity, Translate("messages.updated"));
        }

        [HttpDelete]
        public async Task<IActionResult> Delete(T entity)
        {
            await _service.DeleteAsync(entity);
            await EvictResponseCacheAsync();

            return OkResponse(entity, Translate("messages.deleted"));
        }

        /// <summary>
        /// Translates a key into the language of the current request (see docs/localization.md).
        /// <c>Translate("messages.created")</c>, <c>Translate("welcome.hello", new { name = user.Name })</c>.
        /// </summary>
        protected string Translate(string key, object? replace = null) =>
            HttpContext.RequestServices.GetRequiredService<ITranslator>().Get(key, replace);

        /// <summary>
        /// Clears every cached GET response of this controller ([CacheResponse]).
        /// Called after each successful write; call it from custom write actions too.
        /// </summary>
        protected async Task EvictResponseCacheAsync()
        {
            var store = HttpContext.RequestServices.GetService<IOutputCacheStore>();
            var controller = RouteData.Values["controller"]?.ToString();

            if (store != null && controller != null)
            {
                await store.EvictByTagAsync(
                    ResponseCacheTags.ForController(controller),
                    HttpContext.RequestAborted);
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetByIdAsyn(int id)
        {
            var entity = await _service.GetByIdAsync(id);

            if (entity == null)
                return ErrorResponse(Translate("messages.not_found"), StatusCodes.Status404NotFound);

            return OkResponse(entity, Translate("messages.retrieved"));
        }

        [HttpGet]
        public async Task<IActionResult> GetAllAsync()
        {
            var entities = await _service.GetAllAsync();
            return OkResponse(entities, Translate("messages.retrieved_many"));
        }

        [HttpGet("get")]
        public async Task<IActionResult> Get(
            [ModelBinder(BinderType = typeof(GetRequestModelBinder))]
            GetRequest request)
        {
            var response = await _service.GetAsync(request);

            // A business layer can name a translation key; otherwise its own text is shown as written.
            var message = response.MessageKey != null ? Translate(response.MessageKey) : response.Message;

            if (!response.Success)
                return ErrorResponse(message ?? Translate("errors.request_failed"));

            return Ok(ApiResponse<object?>.SuccessResult(
                response.Data,
                message,
                response.Pagination));
        }

        protected IActionResult OkResponse<TData>(TData data, string? message = null)
        {
            return Ok(ApiResponse<TData>.SuccessResult(data, message));
        }

        protected IActionResult ErrorResponse(
            string message,
            int statusCode = StatusCodes.Status400BadRequest,
            IDictionary<string, string[]>? errors = null)
        {
            return StatusCode(
                statusCode,
                ApiResponse<object?>.Failure(message, errors));
        }

        protected async Task<IActionResult?> ValidateModelAsync<TModel>(TModel model)
        {
            var validator = HttpContext.RequestServices
                .GetService<IValidator<TModel>>();

            if (validator == null)
                return null;

            var result = await validator.ValidateAsync(model);

            if (result.IsValid)
                return null;

            var errors = result.Errors
                .GroupBy(x => x.PropertyName)
                .ToDictionary(
                    x => x.Key,
                    x => x.Select(e => e.ErrorMessage).ToArray()
                );

            return ErrorResponse(
                Translate("errors.validation_failed"),
                StatusCodes.Status400BadRequest,
                errors);
        }
    }
}