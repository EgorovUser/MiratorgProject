using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace XmlParser
{
    /// <summary>
    /// Парсер XML нового формата SAPTableData.
    /// Выбирает только строки с DocType = "RV".
    /// </summary>
    public static class XmlTableParser
    {
        /// <summary>
        /// Парсит XML-строку и возвращает список строк XmlRow,
        /// отфильтрованных по DocType = "RV".
        /// </summary>
        public static List<XmlRow> Parse(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml))
                throw new ArgumentException("XML строка пуста.", nameof(xml));

            var doc = XDocument.Parse(xml);

            var root = doc.Root;
            if (root == null || root.Name.LocalName != "SAPTableData")
                throw new ArgumentException("Неверный формат XML: ожидается корневой элемент <SAPTableData>.");

            var rowsElement = root.Element("Rows");
            if (rowsElement == null)
                return new List<XmlRow>();

            var result = new List<XmlRow>();

            foreach (var rowElement in rowsElement.Elements("Row"))
            {
                int rowIndex = ParseIntSafe(rowElement.Attribute("index")?.Value);

                var values = new Dictionary<string, string>();

                foreach (var cell in rowElement.Elements())
                {
                    values[cell.Name.LocalName] = cell.Value?.Trim();
                }

                // Фильтр: оставляем только строки с DocType = "RV"
                if (!values.TryGetValue("DocType", out var docType) || docType != "RV")
                    continue;

                if (!values.ContainsKey("DocNumber") || !values.ContainsKey("EffectiveAmount"))
                    continue;

                result.Add(new XmlRow(rowIndex, values));
            }

            return result;
        }


        /// <summary>
        /// Удаляет из списка строки, дата в указанном столбце которых
        /// совпадает с текущим годом и месяцем.
        /// Список модифицируется на месте.
        /// </summary>
        /// <param name="rows">Список строк XmlRow (модифицируется).</param>
        /// <param name="dateColumnName">Имя столбца с датой (например, "DocDate").</param>
        public static List<XmlRow> RemoveRowsWithCurrentMonth(List<XmlRow> rows, string dateColumnName)
        {
            if (rows == null)
                throw new ArgumentNullException(nameof(rows));
            if (string.IsNullOrWhiteSpace(dateColumnName))
                throw new ArgumentException("Имя столбца дат не указано.", nameof(dateColumnName));

            var now = DateTime.Now;
            int currentYear = now.Year;
            int currentMonth = now.Month;

            for (int i = rows.Count - 1; i >= 0; i--)
            {
                string dateStr = rows[i].GetValue(dateColumnName);
                if (string.IsNullOrWhiteSpace(dateStr))
                    continue;

                if (TryParseDate(dateStr, out DateTime date))
                {
                    if (date.Year == currentYear && date.Month == currentMonth)
                    {
                        rows.RemoveAt(i);
                    }
                }
            }
            return rows;
        }

        /// <summary>
        /// Пытается распарсить строку в дату. Не бросает исключений.
        /// Поддерживает форматы: dd.MM.yyyy, yyyy-MM-dd, dd/MM/yyyy, yyyyMMdd, unix-время.
        /// </summary>
        private static bool TryParseDate(string raw, out DateTime result)
        {
            result = DateTime.MinValue;

            if (string.IsNullOrWhiteSpace(raw))
                return false;

            raw = raw.Trim();

            // Unix-время
            if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long unixSeconds))
            {
                if (unixSeconds > 0 && unixSeconds < 4_200_000_000)
                {
                    result = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).DateTime;
                    return true;
                }
            }

            string[] formats =
            {
                "dd.MM.yyyy",
                "yyyy-MM-dd",
                "dd/MM/yyyy",
                "MM/dd/yyyy",
                "yyyyMMdd"
            };

            if (DateTime.TryParseExact(raw, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out result))
            {
                return true;
            }

            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
                return true;

            return false;
        }

        private static int ParseIntSafe(string value)
        {
            return int.TryParse(value, out var result) ? result : -1;
        }
    }
}
