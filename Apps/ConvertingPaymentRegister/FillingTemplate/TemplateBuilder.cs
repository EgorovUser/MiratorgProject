using System.Collections.Generic;
using System.Linq;
using CsvParsing;

namespace FillingTemplate
{
    /// <summary>
    /// Построение выходной CSV-таблицы (шаблона) на основе
    /// исходного реестра, конфигурации кредитора и данных из SAP.
    /// </summary>
    public static class TemplateBuilder
    {
        // ---- Заголовки выходного шаблона (9 столбцов, индексы 0-8) ----

        public static readonly string[] TemplateHeaders =
        {
            "БЕ",                    // 0  — фиксированное значение 1440
            "КОД ПОСТАВЩ",           // 1  — код кредитора из SAP
            "НОМЕР ВЗ (72***)",      // 2  — номер взаимозачёта
            "Год",                   // 3  — год из SAP (ДатаДокум)
            "СУММА",                 // 4  — Знач.БЕ из SAP
            "НОМЕР ДОКУМЕНТА",       // 5  — номер документа из реестра / SAP
            "ДАТА СФ",              // 6  — дата счёт-фактуры из SAP
            "ОСТАТОЧНАЯ СУММА СФ",  // 7  — остаточная сумма из SAP
            "ОБЩСУММА"              // 8  — сумма из реестра
        };

        /// <summary>Фиксированное значение БЕ.</summary>
        public const string FixedBE = "1440";

        /// <summary>
        /// Создаёт пустую таблицу-шаблон с заголовками.
        /// </summary>
        public static CsvTable CreateEmpty()
        {
            return new CsvTable(TemplateHeaders.ToList());
        }

        /// <summary>
        /// Создаёт шаблон с одной строкой — только шапка,
        /// для контрагентов без расшифровки (Союз св.Иоанна).
        /// </summary>
        public static CsvTable CreateHeaderOnly(
            string creditorCode,
            string offsetNumber,
            string sapYear,
            string sapSum)
        {
            var table = CreateEmpty();

            var headerRow = new List<string>
            {
                FixedBE,             // 0: БЕ
                creditorCode,        // 1: КОД ПОСТАВЩ
                offsetNumber,        // 2: НОМЕР ВЗ
                sapYear ?? "",       // 3: Год
                sapSum ?? "",        // 4: СУММА
                "",                  // 5: НОМЕР ДОКУМЕНТА
                "",                  // 6: ДАТА СФ
                "",                  // 7: ОСТАТОЧНАЯ СУММА
                ""                   // 8: ОБЩСУММА
            };
            table.AddRow(headerRow);

            return table;
        }

        /// <summary>
        /// Строит строку выходного шаблона по данным строки реестра
        /// и параметрам SAP.
        /// Столбцы «ДАТА СФ» и «ОСТАТОЧНАЯ СУММА СФ» остаются пустыми.
        /// </summary>
        /// <param name="creditorCode">Код кредитора</param>
        /// <param name="offsetNumber">Номер взаимозачёта</param>
        /// <param name="sapYear">Год из SAP</param>
        /// <param name="sapSum">Сумма из SAP (Знач.БЕ)</param>
        /// <param name="docNumber">Номер документа (из реестра или SAP)</param>
        /// <param name="sumValue">Сумма из реестра (ОБЩСУММА)</param>
        public static List<string> BuildRow(
            string creditorCode,
            string offsetNumber,
            string sapYear,
            string sapSum,
            string docNumber,
            string sumValue)
        {
            return new List<string>
            {
                FixedBE,                // 0: БЕ
                creditorCode,           // 1: КОД ПОСТАВЩ
                offsetNumber ?? "",     // 2: НОМЕР ВЗ (72***)
                sapYear ?? "",          // 3: Год
                sapSum ?? "",           // 4: СУММА
                docNumber ?? "",        // 5: НОМЕР ДОКУМЕНТА
                "",                     // 6: ДАТА СФ (не передаётся)
                "",                     // 7: ОСТАТОЧНАЯ СУММА СФ (не передаётся)
                sumValue ?? ""          // 8: ОБЩСУММА
            };
        }
    }
}
