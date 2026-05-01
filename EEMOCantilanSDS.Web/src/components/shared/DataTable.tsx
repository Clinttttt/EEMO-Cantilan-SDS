import { ReactNode } from 'react';
import { EmptyState } from './EmptyState';

interface Column<T> {
  key: string;
  label: string;
  render?: (item: T) => ReactNode;
}

interface DataTableProps<T> {
  data: T[];
  columns: Column<T>[];
  keyExtractor: (item: T) => string;
  emptyMessage?: string;
}

export const DataTable = <T,>({ data, columns, keyExtractor, emptyMessage = 'No records found.' }: DataTableProps<T>) => {
  return (
    <div className="panel">
      {data.length === 0 ? (
        <EmptyState message={emptyMessage} />
      ) : (
        <div className="table-wrap">
          <table className="data-table">
            <thead>
              <tr>
                {columns.map((col) => (
                  <th key={col.key}>{col.label}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {data.map((item) => (
                <tr key={keyExtractor(item)}>
                  {columns.map((col) => (
                    <td key={col.key}>
                      {col.render ? col.render(item) : String((item as Record<string, unknown>)[col.key] ?? '')}
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
};
