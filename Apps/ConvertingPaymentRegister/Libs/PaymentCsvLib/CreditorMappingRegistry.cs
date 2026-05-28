using System;
using System.Collections.Generic;

namespace PaymentCsvLib
{
    /// <summary>
    /// Реестр маппингов: SAP-код кредитора → конфигурация.
    /// Содержит все известные контрагенты и правила переноса
    /// столбцов из реестра в шаблон.
    /// </summary>
    public static class CreditorMappingRegistry
    {
        /// <summary>
        /// Словарь маппингов. Ключ — SAP-код кредитора (строка),
        /// сравнение без учёта регистра.
        /// </summary>
        public static readonly Dictionary<string, CreditorConfig> Mappings;

        static CreditorMappingRegistry()
        {
            Mappings = new Dictionary<string, CreditorConfig>(StringComparer.OrdinalIgnoreCase);

            // =================================================================
            //  Группа 6.1
            //  Агроторг, Агроаспект, Копейка-Москва, Перекресток,
            //  Онлайн-Гипермаркет, Сладкая жизнь Н.Н.
            //
            //  ОБЩСУММА      ← столбец C «Сумма/остаток по документу (в т.ч. НДС)»
            //  НОМЕР ДОКУМЕНТА ← столбец B «Номер документа»
            // =================================================================
            var group61 = new CreditorConfig
            {
                SumSourceColumn = "Сумма/остаток по документу",
                DocNumberSourceColumn = "Номер документа",
                DocDateSourceColumn = "Дата документа"
            };
            Add("1000008109", "ООО «Агроторг»", group61);
            Add("1000002300", "ООО «Агроаспект»", group61);
            Add("1000001890", "Копейка-Москва ООО", group61);
            Add("1000002160", "АО ТД Перекресток", group61);
            Add("1000085418", "Онлайн-Гипермаркет", group61);
            Add("1000009889", "Сладкая жизнь Н.Н.", group61);

            // =================================================================
            //  Группа 6.2
            //  Ашан, Атак (и Ритейл Проперти 6 — тот же SAP-код)
            //
            //  ОБЩСУММА      ← столбец E «Размер зачетного требования»
            //  НОМЕР ДОКУМЕНТА ← столбец A «Номер УПД»
            //  Исключать строки со словом «Итого»
            // =================================================================
            var group62 = new CreditorConfig
            {
                // «Размер зачтенного требования» — в файле Ашан используется
                // форма «зачтенного» (не «зачетного»), синонимы обрабатываются в парсере
                SumSourceColumn = "Размер зачтенного требования",
                DocNumberSourceColumn = "Номер УПД",
                DocDateSourceColumn = "Дата УПД",
                ExcludeRowKeywords = new[] { "Итого" }
            };
            Add("1000008093", "Ашан", group62);
            Add("1000018164", "Атак / Ритейл Проперти 6", group62);

            // =================================================================
            //  Группа 6.3 — Дикси
            //
            //  ОБЩСУММА      ← столбец E «Сумма с НДС»
            //  НОМЕР ДОКУМЕНТА ← SAP ZSSD_120_VBRP
            //  Исключать строки с «ИТОГО»
            // =================================================================
            Add("1000002250", "Дикси", new CreditorConfig
            {
                SumSourceColumn = "Сумма с НДС",
                DocNumberSourceColumn = "Номер документа",
                DocDateSourceColumn = "Дата документа",
                UseSapForDocNumber = true,
                ExcludeRowKeywords = new[] { "ИТОГО", "Номер и дата" }
            });

            // =================================================================
            //  Группа 6.4 — Гипер Глобус
            //
            //  Две таблицы рядом: левая (Гиперглобус) и правая (ТК Мираторг).
            //  Заголовок: Дата док-та;№ док-та;Сумма (С НДС);Дата док-та;№ док-та;Сумма (С НДС)
            //  Нужна ПРАВАЯ часть (столбцы 3-5) — документы ТК Мираторг.
            //
            //  ОБЩСУММА      ← столбец F (индекс 5) «Сумма (С НДС)» — второе вхождение
            //  НОМЕР ДОКУМЕНТА ← столбец E (индекс 4) «№ док-та» — второе вхождение
            //  Исключать: «Сумма зачета…» и дублирующие заголовки
            // =================================================================
            Add("1000002088", "ГиперГлобус", new CreditorConfig
            {
                SumSourceColumnIndex = 5,
                DocNumberSourceIndex = 4,
                DocDateSourceIndex = 3,
                DotAsThousandsSeparator = true,
                ExcludeRowKeywords = new[] { "Сумма зачета", "Задолженность", "Дата док-та" }
            });

            // =================================================================
            //  Группа 6.5 — Метро
            //
            //  Столбцы без стандартных заголовков — маппинг по индексу:
            //  ОБЩСУММА      ← столбец E (индекс 4) «Сумма с НДС»
            //  НОМЕР ДОКУМЕНТА ← столбец A (индекс 0)
            //  Дата документа ← столбец B (индекс 1)
            //  Формат чисел: точка — разделитель тысяч, хвостовой минус
            // =================================================================
            Add("1000002178", "Метро", new CreditorConfig
            {
                SumSourceColumnIndex = 4,
                DocNumberSourceIndex = 0,
                DocDateSourceIndex = 1,
                DotAsThousandsSeparator = true,
                TrailingMinus = true
            });

            // =================================================================
            //  Группа 6.6 — Лента
            //
            //  ОБЩСУММА      ← столбец G «К зачету, руб»
            //  НОМЕР ДОКУМЕНТА ← столбец E «№ документа»
            //  Исключать строки с «Итого»
            // =================================================================
            Add("1000009978", "Лента", new CreditorConfig
            {
                SumSourceColumn = "К зачету, руб",
                DocNumberSourceColumn = "№ документа",
                DocDateSourceColumn = "Дата документа",
                ExcludeRowKeywords = new[] { "Итого" }
            });

            // =================================================================
            //  Группа 6.7 — ООО Окей
            //
            //  ОБЩСУММА      ← столбец D «Сумма, руб»
            //  НОМЕР ДОКУМЕНТА ← столбец C «№ счета-фактуры»,
            //                     если пусто — столбец B «№ накладной»
            //  Исключать строки с «Всего»
            // =================================================================
            Add("1000000000", "ООО Окей", new CreditorConfig
            {
                SumSourceColumn = "Сумма, руб",
                DocNumberSourceColumn = "№ счета-фактуры",
                DocNumberFallbackColumn = "№ накладной",
                ExcludeRowKeywords = new[] { "Всего" }
            });

            // =================================================================
            //  Группа 6.8 — Виктория Балтия
            //
            //  ОБЩСУММА      ← столбец G «Сумма, руб» (только В/Сч = «K»)
            //  НОМЕР ДОКУМЕНТА ← столбец D «Номер и дата с/ф Поставщика»
            //                     (извлечь только номер — часть до даты)
            // =================================================================
            Add("1000003219", "Виктория Балтия", new CreditorConfig
            {
                SumSourceColumn = "Сумма, руб",
                // В файле столбец называется «№ и дата с/ф Поставщика»,
                // но синоним «Номер ↔ №» обрабатывается парсером
                DocNumberSourceColumn = "№ и дата с/ф Поставщика",
                FilterColumnName = "В/Сч",
                FilterValue = "K",
                ParseDocNumberBeforeDate = true
            });

            // =================================================================
            //  Группа 6.9 — Союз св.Иоанна
            //
            //  Реестр без расшифровки — только сумма взаимозачёта.
            //  Документы подбираются алгоритмом п.7.
            // =================================================================
            Add("1000000001", "Союз св.Иоанна", new CreditorConfig
            {
                SumSourceColumn = "Сумма по документу, принимаемая к зачету Без НДС",
                ExcludeRowKeywords = new[] { "Итого" },
                UseSapForDocNumber = true
            });

            // =================================================================
            //  Группа 6.10 — Тандер
            //
            //  ОБЩСУММА      ← столбец G «Зачет руб»
            //  НОМЕР ДОКУМЕНТА ← столбец E «СФ»
            //  Исключать строки с «Итого»
            // =================================================================
            Add("1000002213", "Тандер", new CreditorConfig
            {
                SumSourceColumn = "Зачет руб",
                DocNumberSourceColumn = "СФ",
                DocDateSourceColumn = "Дата отгрузки",
                ExcludeRowKeywords = new[] { "Итого" }
            });

            // =================================================================
            //  Ле Монлид
            //
            //  Нет отдельного алгоритма — будет использован
            //  поиск по контексту (fallback).
            // =================================================================
            Add("1000073095", "Ле Монлид", new CreditorConfig
            {
                // Без явного маппинга столбцов — fallback по контексту
            });
        }

        /// <summary>
        /// Добавляет маппинг, создавая копию template-конфига.
        /// </summary>
        private static void Add(string code, string name, CreditorConfig template)
        {
            var config = new CreditorConfig
            {
                CreditorName = name,
                SumSourceColumn = template.SumSourceColumn,
                SumSourceColumnIndex = template.SumSourceColumnIndex,
                DocNumberSourceColumn = template.DocNumberSourceColumn,
                DocNumberSourceIndex = template.DocNumberSourceIndex,
                DocNumberFallbackColumn = template.DocNumberFallbackColumn,
                FilterColumnName = template.FilterColumnName,
                FilterValue = template.FilterValue,
                ExcludeRowKeywords = template.ExcludeRowKeywords,
                UseSapForDocNumber = template.UseSapForDocNumber,
                ParseDocNumberBeforeDate = template.ParseDocNumberBeforeDate,
                NoRegistryDetails = template.NoRegistryDetails,
                DotAsThousandsSeparator = template.DotAsThousandsSeparator,
                TrailingMinus = template.TrailingMinus,
                DocDateSourceColumn = template.DocDateSourceColumn,
                DocDateSourceIndex = template.DocDateSourceIndex
            };
            Mappings[code] = config;
        }

        /// <summary>
        /// Возвращает конфигурацию по SAP-коду или null, если код не найден.
        /// </summary>
        public static CreditorConfig FindByCode(string creditorCode)
        {
            if (Mappings.TryGetValue(creditorCode, out CreditorConfig config))
                return config;
            return null;
        }
    }
}
