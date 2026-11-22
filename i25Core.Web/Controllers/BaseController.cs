using FluentValidation;
using i25Core.Web.Helpers;
using i25Core.BusinessLayer;
using i25Core.BusinessLayer.Implementation;
using i25Core.Core.DTO;
using i25Core.Web.Caching;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using System.Text.Json;


namespace i25Core.Web.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
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

            return OkResponse(entity, "Record created successfully.");
        }

        [HttpPut]
        public async Task<IActionResult> Update([FromBody] T entity)
        {
            var validation = await ValidateModelAsync(entity);

            if (validation != null)
                return validation;

            await _service.UpdateSync(entity);
            await EvictResponseCacheAsync();

            return OkResponse(entity, "Record updated successfully.");
        }

        [HttpDelete]
        public async Task<IActionResult> Delete(T entity)
        {
            await _service.DeleteAsync(entity);
            await EvictResponseCacheAsync();

            return OkResponse(entity, "Record deleted successfully.");
        }

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
                return ErrorResponse("Record not found.", StatusCodes.Status404NotFound);

            return OkResponse(entity, "Record retrieved successfully.");
        }

        [HttpGet]
        public async Task<IActionResult> GetAllAsync()
        {
            var entities = await _service.GetAllAsync();
            return OkResponse(entities, "Records retrieved successfully.");
        }

        [HttpGet("get")]
        public async Task<IActionResult> Get(
            [ModelBinder(BinderType = typeof(GetRequestModelBinder))]
            GetRequest request)
        {
            var response = await _service.GetAsync(request);

            if (!response.Success)
                return ErrorResponse(response.Message ?? "The request could not be completed.");

            return Ok(ApiResponse<object?>.SuccessResult(
                response.Data,
                response.Message));
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
                "Validation failed.",
                StatusCodes.Status400BadRequest,
                errors);
        }
    }
}