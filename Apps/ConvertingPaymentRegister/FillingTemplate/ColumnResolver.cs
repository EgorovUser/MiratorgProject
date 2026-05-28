using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using CsvParsing;
using PaymentCsvLib;

namespace FillingTemplate
{
    /// <summary>
    /// Разрешение имён/индексов столбцов исходной таблицы.
    /// Поддерживает нечёткий поиск по имени и fallback по контексту
    /// (по содержимому ячеек).
    /// </summary>
    public static class ColumnResolver
    {
        /// <summary>
        /// Результат разрешения столбцов.
        /// </summary>
        public class ResolvedColumns
        {
            public int SumColIdx = -1;
            public int DocNumColIdx = -1;
            public int DocNumFallbackIdx = -1;
            public int FilterColIdx = -1;
        }

        /// <summary>
        /// Разрешает индексы столбцов по конфигурации кредитора.
        /// Если по имени найти не удалось — использует fallback по контексту.
        /// </summary>
        /// <param name="sourceTable">Исходная CSV-таблица</param>
        /// <param name="config">Конфигурация кредитора</param>
        /// <param name="useContextFallback">Принудительно использовать fallback</param>
        public static ResolvedColumns Resolve(
            CsvTable sourceTable,
            CreditorConfig config,
            bool useContextFallback)
        {
            var result = new ResolvedColumns();
            bool anyFound = false;

            // --- Столбец суммы ---
            if (config.SumSourceColumnIndex != -1)
            {
                result.SumColIdx = config.SumSourceColumnIndex;
                if (result.SumColIdx < sourceTable.ColumnCount)
                    anyFound = true;
            }
            else if (!string.IsNullOrWhiteSpace(config.SumSourceColumn))
            {
                result.SumColIdx = FindColumnByFuzzyName(sourceTable.Headers, config.SumSourceColumn);
                if (result.SumColIdx >= 0)
                    anyFound = true;
            }

            // --- Столбец номера документа ---
            if (config.DocNumberSourceIndex != -1)
            {
                result.DocNumColIdx = config.DocNumberSourceIndex;
                if (result.DocNumColIdx >= sourceTable.ColumnCount)
                    result.DocNumColIdx = -1;
            }
            else if (!string.IsNullOrWhiteSpace(config.DocNumberSourceColumn))
            {
                result.DocNumColIdx = FindColumnByFuzzyName(sourceTable.Headers, config.DocNumberSourceColumn);
            }

            // --- Резервный столбец номера документа ---
            if (!string.IsNullOrWhiteSpace(config.DocNumberFallbackColumn))
            {
                result.DocNumFallbackIdx = FindColumnByFuzzyName(
                    sourceTable.Headers, config.DocNumberFallbackColumn);
            }

            // --- Столбец фильтра ---
            if (!string.IsNullOrWhiteSpace(config.FilterColumnName))
            {
                result.FilterColIdx = FindColumnByFuzzyName(
                    sourceTable.Headers, config.FilterColumnName);
            }

            // --- Fallback по контексту ---
            if (!anyFound || useContextFallback)
            {
                if (result.SumColIdx < 0)
                    result.SumColIdx = FindSumColumn(sourceTable);

                if (result.DocNumColIdx < 0)
                    result.DocNumColIdx = FindDocNumberColumn(sourceTable);
            }

            return result;
        }

        // =====================================================================
        //  Нечёткий поиск столбца по имени
        // =====================================================================

        /// <summary>
        /// Ищет столбец по имени с нормализацией:
        /// - приводим к нижнему регистру;
        /// - убираем лишние пробелы, переводы строк, знаки препинания;
        /// - проверяем вхождение искомого имени в имя заголовка.
        /// </summary>
        public static int FindColumnByFuzzyName(List<string> headers, string searchName)
        {
            if (string.IsNullOrWhiteSpace(searchName))
                return -1;

            string normalizedSearch = NormalizeForMatch(searchName);

            // 1. Точное совпадение (после нормализации)
            for (int i = 0; i < headers.Count; i++)
            {
                if (NormalizeForMatch(headers[i]) == normalizedSearch)
                    return i;
            }

            // 2. Вхождение искомого в заголовок
            for (int i = 0; i < headers.Count; i++)
            {
                string normalizedHeader = NormalizeForMatch(headers[i]);
                if (normalizedHeader.Contains(normalizedSearch))
                    return i;
            }

            // 3. Вхождение заголовка в искомое
            for (int i = 0; i < headers.Count; i++)
            {
                string normalizedHeader = NormalizeForMatch(headers[i]);
                if (normalizedSearch.Contains(normalizedHeader) && normalizedHeader.Length > 2)
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// Нормализует строку для сравнения: нижний регистр,
        /// удаление лишних пробелов, переносов строк, скобок.
        /// </summary>
        public static string NormalizeForMatch(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return "";

            // Убираем переводы строк, табуляции
            s = s.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");

            // Убираем лишние символы, мешающие сравнению
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                if (c == '(' || c == ')' || c == '"' || c == '\'' || c == '/')
                    sb.Append(' ');
                else
                    sb.Append(c);
            }

            // Схлопываем пробелы
            return Regex.Replace(sb.ToString(), @"\s+", " ").Trim().ToLowerInvariant();
        }

        // =====================================================================
        //  Fallback: поиск столбцов по контексту данных
        // =====================================================================

        /// <summary>
        /// Ищет столбец с суммами: значения преимущественно числовые
        /// (могут содержать разделители тысяч, десятичную запятую/точку).
        /// Условие: более 50% непустых строк — числа.
        /// </summary>
        public static int FindSumColumn(CsvTable table)
        {
            int bestCol = -1;
            double bestRatio = 0.5;

            for (int col = 0; col < table.ColumnCount; col++)
            {
                int totalNonEmpty = 0;
                int numericCount = 0;

                foreach (var row in table.Rows)
                {
                    if (col >= row.CellCount) continue;
                    string val = (row[col] ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(val)) continue;

                    totalNonEmpty++;

                    // Убираем знак, пробелы-разделители тысяч
                    string normalized = val.Replace(" ", "").TrimStart('+');
                    if (normalized.StartsWith("-"))
                        normalized = normalized.Substring(1);

                    // Число: цифры, возможная десятичная запятая/точка
                    if (Regex.IsMatch(normalized, @"^[\d]+([,\.][\d]+)?$"))
                        numericCount++;
                }

                if (totalNonEmpty == 0) continue;

                double ratio = (double)numericCount / totalNonEmpty;
                if (ratio > bestRatio)
                {
                    bestRatio = ratio;
                    bestCol = col;
                }
            }

            return bestCol;
        }

        /// <summary>
        /// Ищет столбец с номерами документов: более 50% непустых
        /// строк содержат только цифры (без запятых и точек).
        /// </summary>
        public static int FindDocNumberColumn(CsvTable table)
        {
            int bestCol = -1;
            double bestRatio = 0.5;

            for (int col = 0; col < table.ColumnCount; col++)
            {
                int totalNonEmpty = 0;
                int matchCount = 0;

                foreach (var row in table.Rows)
                {
                    if (col >= row.CellCount) continue;
                    string val = (row[col] ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(val)) continue;

                    totalNonEmpty++;

                    // Только цифры, без запятых, точек, букв
                    if (Regex.IsMatch(val, @"^\d+$"))
                        matchCount++;
                }

                if (totalNonEmpty == 0) continue;

                double ratio = (double)matchCount / totalNonEmpty;
                if (ratio > bestRatio)
                {
                    bestRatio = ratio;
                    bestCol = col;
                }
            }

            return bestCol;
        }
    }
}
