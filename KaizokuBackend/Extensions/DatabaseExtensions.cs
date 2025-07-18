using KaizokuBackend.Data;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace KaizokuBackend.Extensions
{
    /// <summary>
    /// Extension methods for database operations
    /// </summary>
    public static class DatabaseExtensions
    {
        private class SeriesPathInfo
        {
            public string StoragePath { get; set; } = string.Empty;
            public Guid Id { get; set; }
        }

        /// <summary>
        /// Gets a dictionary mapping storage paths to series IDs
        /// </summary>
        /// <param name="db">The database context</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>Dictionary mapping storage paths to series IDs</returns>
        public static async Task<Dictionary<string, Guid>> GetPathsAsync(this AppDbContext db, CancellationToken token = default)
        {
            List<SeriesPathInfo> results = await db.Series
                .AsNoTracking()
                .Select(a => new SeriesPathInfo { Id = a.Id, StoragePath = a.StoragePath })
                .ToListAsync(token)
                .ConfigureAwait(false);

            Dictionary<string, Guid> paths = new Dictionary<string, Guid>(StringComparer.InvariantCultureIgnoreCase);
            foreach (var item in results)
            {
                string sanitizedPath = item.StoragePath.SanitizeDirectory();
                if (!paths.ContainsKey(sanitizedPath))
                {
                    paths[sanitizedPath] = item.Id;
                }
            }
            return paths;
        }

        /// <summary>
        /// Marks a property as modified in Entity Framework change tracking
        /// </summary>
        /// <typeparam name="TEntity">Entity type</typeparam>
        /// <typeparam name="TProperty">Property type</typeparam>
        /// <param name="db">The database context</param>
        /// <param name="entity">The entity instance</param>
        /// <param name="propertyExpression">Expression pointing to the property</param>
        public static void Touch<TEntity, TProperty>(this AppDbContext db, TEntity entity, Expression<Func<TEntity, TProperty>> propertyExpression) 
            where TEntity : class
        {
            if (!db.Entry(entity).Property(propertyExpression).IsModified)
            {
                db.Entry(entity).Property(propertyExpression).IsModified = true;
            }
        }
    }
}