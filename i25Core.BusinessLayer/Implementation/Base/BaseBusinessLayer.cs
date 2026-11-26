using i25Core.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using i25Core.BusinessLayer.Implementation.Interface;
using i25Core.BusinessLayer.Extensions;
using i25Core.BusinessLayer.Parameters;
using i25Core.Core.Exceptions;
using i25Core.Core.DTO;

namespace i25Core.BusinessLayer.Implementation.Base
{
    public class BaseBusinessLayer<T> : IBusinessLayer<T> where T : class
    {
        protected readonly I25DbContext _context;
        protected readonly DbSet<T> _dbSet;

        public BaseBusinessLayer(IRepository db)
        {
            _context = db.Context;
            _dbSet = db.GetQuery<T>();
        }

        public virtual async Task AddAsync(T entity, CancellationToken ct = default)
        {
           await _dbSet.AddAsync(entity, ct);
           await SaveAsync(entity, ct);
        }

        public virtual async Task UpdateSync(T entity, CancellationToken ct = default)
        {
            var existing = await _dbSet.FindAsync(FindPrimaryKey(entity), ct);

            if (existing == null)
            {
                throw NotFoundException.ForEntity(typeof(T).Name);
            }

            _context.Entry(existing).CurrentValues.SetValues(entity);

            await SaveAsync(entity, ct);
        }
        public virtual async Task SaveAsync(T entity, CancellationToken ct = default)
        {   
            await _context.SaveChangesAsync(ct);
        }


        public virtual async Task<T?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            return await _dbSet.FindAsync(new object[] { id }, ct);        
        }       

        public virtual async Task<IEnumerable<T>> GetAllAsync(CancellationToken ct = default)
        {           
            return await _dbSet.ToListAsync(ct);
        }

        public async Task DeleteAsync(T entity, CancellationToken ct = default)
        {          
            var model = await _dbSet.FindAsync(FindPrimaryKey(entity), ct);
            if (model != null)
            {
                _dbSet.Remove(model);
                await SaveAsync(entity, ct);
            } 
        }  

        public async Task<bool> ExecuteStoredProcedureAsync(string procedureName,  params SqlParameter[] parameters)
        {
            // Errors (including RAISERROR/THROW from the procedure) bubble up to
            // GlobalExceptionHandler, which logs them and maps them to an ApiResponse.
            var parameterNames = string.Join(", ",
                parameters.Select(p => $"@{p.ParameterName.TrimStart('@')}"));

            var sql = $"EXEC {procedureName} {parameterNames}";

            await _context.Database.ExecuteSqlRawAsync(sql, parameters);

            return true;
        }
        private object?[] FindPrimaryKey(T entity)
        {
            var entityType = _context.Model.FindEntityType(typeof(T));

            var primaryKey = entityType?.FindPrimaryKey()
                ?? throw new InvalidOperationException(
                    $"{typeof(T).Name} does not have a primary key.");

            return primaryKey.Properties
                .Select(property =>
                    _context.Entry(entity)
                        .Property(property.Name).CurrentValue)
                .ToArray();
        }

        public virtual async Task<GetResponse> GetAsync(GetRequest request, CancellationToken ct = default)
        {
            IQueryable<T> query = _dbSet;       

            if (request?.Filters != null && request.Filters.Any())
            {
                query = query.Filter(request.Filters);
            }

            if (request?.Sorts?.Any() == true)
                query = query.Sort(request.Sorts);

            if (request?.IsPaged != true)
            {
                return new GetResponse
                {
                    Success = true,
                    Message = "Records retrieved successfully.",
                    MessageKey = "messages.retrieved_many",
                    Data = await query.ToListAsync(ct)
                };
            }

            var page = request.ResolvedPage;
            var pageSize = request.ResolvedPageSize;

            // Count after filtering, before Skip/Take.
            var totalCount = await query.CountAsync(ct);

            // Skip/Take needs a stable order; fall back to the primary key.
            if (request.Sorts?.Any() != true)
                query = query.Sort(DefaultSort());

            var data = await query.Paginate(page, pageSize).ToListAsync(ct);

            return new GetResponse
            {
                Success = true,
                Message = "Records retrieved successfully.",
                MessageKey = "messages.retrieved_many",
                Data = data,
                Pagination = PaginationMeta.Create(page, pageSize, totalCount)
            };
        }

        private IEnumerable<GetSort> DefaultSort()
        {
            var keyProperties = _context.Model.FindEntityType(typeof(T))?
                .FindPrimaryKey()?
                .Properties;

            return keyProperties?.Select(p => new GetSort { Column = p.Name })
                ?? Enumerable.Empty<GetSort>();
        }
    }
}
