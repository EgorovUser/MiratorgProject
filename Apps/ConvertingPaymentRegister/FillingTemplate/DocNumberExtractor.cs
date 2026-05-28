using System.Text.RegularExpressions;

namespace FillingTemplate
{
    /// <summary>
    /// Извлечение номера документа из составных строк.
    /// Используется для контрагентов, у которых в одном столбце
    /// указан и номер, и дата (например, Виктория Балтия).
    /// </summary>
    public static class DocNumberExtractor
    {
        /// <summary>
        /// Из строки вида «УПД-12345 от 15.03.2024» или
        /// «12345 15.03.2024» извлекает только номер
        /// (всё, что до первой даты или слова «от»).
        /// </summary>
        /// <param name="raw">Исходная строка с номером и/или датой</param>
        /// <returns>Извлечённый номер документа</returns>
        public static string ExtractBeforeDate(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "";

            raw = raw.Trim();

            // Пытаемся найти дату в формате dd.MM.yyyy или dd/MM/yyyy
            var dateMatch = Regex.Match(raw, @"\d{1,2}[./]\d{1,2}[./]\d{2,4}");
            if (dateMatch.Success)
            {
                string beforeDate = raw.Substring(0, dateMatch.Index).Trim();

                // Убираем предлог «от» в конце
                beforeDate = Regex.Replace(beforeDate, @"\s+от\s*$", "", RegexOptions.IgnoreCase).Trim();

                if (!string.IsNullOrWhiteSpace(beforeDate))
                    return beforeDate;
            }

            // Если даты нет — пытаемся отрезать по слову «от»
            var otMatch = Regex.Match(raw, @"\s+от\s+", RegexOptions.IgnoreCase);
            if (otMatch.Success)
            {
                string beforeOt = raw.Substring(0, otMatch.Index).Trim();
                if (!string.IsNullOrWhiteSpace(beforeOt))
                    return beforeOt;
            }

            // Если не смогли распарсить — возвращаем как есть
            return raw;
        }
    }
}
