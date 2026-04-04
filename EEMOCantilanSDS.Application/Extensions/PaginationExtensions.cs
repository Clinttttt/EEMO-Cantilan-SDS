using EEMOCantilanSDS.Domain.Common;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Application.Extensions
{
    public static class PaginationExtensions
    {
        public static async Task<CursorPagedResult<T>> ToCursorPagedResultAsync<T>(this IQueryable<T> query, int page_size, Func<T, DateTime?> cursorSelector, CancellationToken cancellationToken)
        {
            var items = await query.Take(page_size + 1).ToListAsync(cancellationToken);
            var hasMore = items.Count > page_size;
            if (hasMore) items.RemoveAt(items.Count - 1);
            var nextCursor = hasMore ? cursorSelector(items.Last()) : null;
            return new CursorPagedResult<T>
            {
                Items = items.Take(page_size).ToList(),
                NextCursor = nextCursor,
                HasMore = hasMore
            };

        }
    }
}
