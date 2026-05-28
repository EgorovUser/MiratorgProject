using CsvParsing;
using PaymentCsvLib;

namespace FillingTemplate
{
    /// <summary>
    /// Фильтрация строк исходного реестра.
    /// Определяет, нужно ли исключить строку из обработки.
    /// </summary>
    public static class RowFilter
    {
        /// <summary>
        /// Возвращает true, если строку нужно исключить из обработки.
        /// </summary>
        /// <param name="row">Строка исходной таблицы</param>
        /// <param name="config">Конфигурация кредитора</param>
        /// <param name="filterColIdx">
        /// Индекс столбца фильтрации (из ColumnResolver).
        /// -1, если фильтр не используется.
        /// </param>
        public static bool ShouldExclude(CsvRow row, CreditorConfig config, int filterColIdx)
        {
            // 1. Фильтр по значению столбца (Виктория Балтия: В/Сч = «К»)
            if (!string.IsNullOrWhiteSpace(config.FilterColumnName) && filterColIdx >= 0)
            {
                string filterVal = (row[filterColIdx] ?? "").Trim();
                if (!string.Equals(filterVal, config.FilterValue, System.StringComparison.OrdinalIgnoreCase))
                    return true; // Строка не проходит фильтр — исключаем
            }

            // 2. Исключение по ключевым словам (Итого, Всего, …)
            if (config.ExcludeRowKeywords != null && config.ExcludeRowKeywords.Length > 0)
            {
                for (int col = 0; col < row.CellCount; col++)
                {
                    string cellValue = (row[col] ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(cellValue))
                        continue;

                    foreach (var keyword in config.ExcludeRowKeywords)
                    {
                        if (cellValue.IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) >= 0)
                            return true;
                    }
                }
            }

            return false;
        }
    }
}
