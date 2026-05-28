using System;

namespace PaymentCsvLib
{
    /// <summary>
    /// Конфигурация маппинга для конкретного кредитора.
    /// Описывает, из каких столбцов реестра брать сумму и номер документа,
    /// а также фильтры и особые режимы парсинга.
    /// </summary>
    public class CreditorConfig
    {
        /// <summary>
        /// Наименование кредитора (для логирования и отладки).
        /// </summary>
        public string CreditorName { get; set; }

        /// <summary>
        /// Название столбца с суммой (частичное совпадение).
        /// Если null — используется SumSourceColumnIndex.
        /// </summary>
        public string SumSourceColumn { get; set; }

        /// <summary>
        /// Индекс столбца с суммой (0-based).
        /// Используется, если SumSourceColumn не задан или
        /// когда в заголовке есть дубликаты (например, ГиперГлобус).
        /// </summary>
        public int SumSourceColumnIndex { get; set; } = -1;

        /// <summary>
        /// Название столбца с номером документа (частичное совпадение).
        /// Если null — используется DocNumberSourceIndex.
        /// </summary>
        public string DocNumberSourceColumn { get; set; }

        /// <summary>
        /// Индекс столбца с номером документа (0-based).
        /// </summary>
        public int DocNumberSourceIndex { get; set; } = -1;

        /// <summary>
        /// Запасной столбец для номера документа.
        /// Если DocNumberSourceColumn пуст — берётся из этого столбца.
        /// </summary>
        public string DocNumberFallbackColumn { get; set; }

        /// <summary>
        /// Название столбца-фильтра (например, «В/Сч» для Виктории Балтии).
        /// </summary>
        public string FilterColumnName { get; set; }

        /// <summary>
        /// Значение фильтра (например, «К» — только кредитовые строки).
        /// </summary>
        public string FilterValue { get; set; }

        /// <summary>
        /// Ключевые слова для исключения строк (Итого, Всего и т.п.).
        /// </summary>
        public string[] ExcludeRowKeywords { get; set; }

        /// <summary>
        /// Если true — номер документа берётся из SAP (ZSSD_120_VBRP),
        /// а не из столбца реестра (Дикси).
        /// </summary>
        public bool UseSapForDocNumber { get; set; }

        /// <summary>
        /// Если true — из значения столбца номера документа нужно
        /// извлечь только часть до даты (Виктория Балтия:
        /// «8031937940 06.12.2024» → «8031937940»).
        /// </summary>
        public bool ParseDocNumberBeforeDate { get; set; }

        /// <summary>
        /// Если true — реестр не содержит расшифровки документов,
        /// только общая сумма взаимозачёта (Союз св.Иоанна).
        /// </summary>
        public bool NoRegistryDetails { get; set; }

        /// <summary>
        /// Если true — в файле используется точка как разделитель
        /// тысяч (например, «1.535.138,50» — ГиперГлобус, Метро).
        /// </summary>
        public bool DotAsThousandsSeparator { get; set; }

        /// <summary>
        /// Если true — отрицательные числа имеют минус в конце
        /// (например, «317.436,63-» — Метро).
        /// </summary>
        public bool TrailingMinus { get; set; }

        /// <summary>
        /// Название столбца с датой документа (опционально).
        /// Если не задано — дата не извлекается.
        /// </summary>
        public string DocDateSourceColumn { get; set; }

        /// <summary>
        /// Индекс столбца с датой документа (0-based).
        /// Используется, если DocDateSourceColumn не задан.
        /// </summary>
        public int DocDateSourceIndex { get; set; } = -1;
    }
}
