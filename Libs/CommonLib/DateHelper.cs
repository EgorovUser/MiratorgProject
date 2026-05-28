using System;
using System.Globalization;

namespace CommonLib
{
    /// <summary>
    /// Утилиты для парсинга дат в различных форматах.
    /// Поддерживаемые форматы: yyyy-MM-dd, dd.MM.yyyy, dd/MM/yyyy.
    /// Также есть fallback на DateTime.TryParse (auto-detect по культуре).
    /// </summary>
    public static class DateHelper
    {
        /// <summary>
        /// Поддерживаемые форматы дат для явного парсинга.
        /// </summary>
        private static readonly string[] SupportedFormats =
        {
            "dd-MM-yyyy",
            "dd.MM.yyyy",
            "dd/MM/yyyy"
        };
        /// <summary>
        /// Культура для чисел:
        /// - разделитель тысячных: пробел
        /// - десятичный разделитель: запятая
        /// </summary>
        private static readonly CultureInfo NumberCulture;

        static DateHelper()
        {
            NumberCulture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            NumberCulture.NumberFormat.NumberDecimalSeparator = ",";
            NumberCulture.NumberFormat.NumberGroupSeparator = " ";
            NumberCulture.NumberFormat.NumberGroupSizes = new[] { 3 };
        }

        /// <summary>
        /// Пытается распарсить строку в DateTime.
        /// Сначала пробуются форматы из <see cref="SupportedFormats"/>,
        /// затем — DateTime.TryParse (auto-detect по текущей культуре).
        /// </summary>
        /// <param name="dateStr">Строка с датой</param>
        /// <param name="result">Результат парсинга (default при неудаче)</param>
        /// <returns>true, если парсинг успешен</returns>
        public static bool TryParse(string dateStr, out DateTime result)
        {
            result = default;

            if (string.IsNullOrWhiteSpace(dateStr))
                return false;

            string trimmed = dateStr.Trim();

            // Явные форматы
            if (DateTime.TryParseExact(
                    trimmed,
                    SupportedFormats,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out result))
            {
                return true;
            }

            // Fallback — auto-parse по культуре
            return DateTime.TryParse(trimmed, out result);
        }

        /// <summary>
        /// Пытается распарсить строку в DateTime.
        /// Возвращает null при неудаче.
        /// </summary>
        /// <param name="dateStr">Строка с датой</param>
        /// <returns>DateTime? — результат парсинга или null</returns>
        public static DateTime? TryParseNullable(string dateStr)
        {
            if (TryParse(dateStr, out DateTime result))
                return result;

            return null;
        }

        /// <summary>
        /// Пытается распарсить строку в decimal.
        /// Поддерживает:
        /// - знак +/-
        /// - пробел как разделитель тысячных
        /// - запятую как десятичный разделитель
        /// Примеры: "1 234,56", "-12,5", "+10 000,00"
        /// </summary>
        public static bool TryParseDecimal(string value, out decimal result)
        {
            result = default;

            if (string.IsNullOrWhiteSpace(value))
                return false;

            string normalized = value.Trim();

            return decimal.TryParse(
                normalized,
                NumberStyles.Number,
                NumberCulture,
                out result);
        }

        /// <summary>
        /// Пытается распарсить строку в decimal.
        /// Возвращает null при неудаче.
        /// </summary>
        public static decimal? TryParseDecimalNullable(string value)
        {
            if (TryParseDecimal(value, out decimal result))
                return result;

            return null;
        }

        /// <summary>
        /// Форматирует decimal в строку:
        /// - знак сохраняется автоматически
        /// - десятичный разделитель: запятая
        /// - разделитель тысячных: пробел
        /// </summary>
        /// <param name="value">Число</param>
        /// <param name="decimalPlaces">Количество знаков после запятой</param>
        public static string ToStringDecimal(decimal value, int decimalPlaces = 2)
        {
            if (decimalPlaces < 0)
                throw new ArgumentOutOfRangeException(nameof(decimalPlaces));

            string format = decimalPlaces == 0 ? "N0" : $"N{decimalPlaces}";
            return value.ToString(format, NumberCulture);
        }
    }
}
