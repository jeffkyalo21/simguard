/**
 * The numbers behind a chart as a table that only screen readers see, so the
 * chart is not the only way to get them.
 * @param {{ caption: string, columns: { key: string, header: string }[], rows: Record<string, any>[] }} props
 */
export function ChartTable({ caption, columns, rows }) {
  return (
    <table className="sr-only">
      <caption>{caption}</caption>
      <thead>
        <tr>
          {columns.map((column) => (
            <th key={column.key} scope="col">
              {column.header}
            </th>
          ))}
        </tr>
      </thead>
      <tbody>
        {rows.map((row, index) => (
          <tr key={index}>
            {columns.map((column) => (
              <td key={column.key}>{row[column.key]}</td>
            ))}
          </tr>
        ))}
      </tbody>
    </table>
  );
}
