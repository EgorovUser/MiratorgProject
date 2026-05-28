using CommonLib;
using CsvParsing;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FindAndFixErrors1_6App
{
    /// <summary>Имена параметров, передаваемых через IDictionary</summary>
    public static class Parameters
    {
        public const string InputFilePath = "inputFilePath";
        public const string OutputFilePath = "outputFilePath";
        public const string Delimiter = "delimiter";

        public const string Result = "result";
    }

    /// <summary>
    /// Конфигурация для CsvProcessorNew.
    /// Содержит таблицу соответствия имён дебиторов и их кодов.
    /// Специфична для данного проекта — не выносится в CsvLib.
    /// </summary>
    public class Config
    {
        public string InputFilePath { get; }
        public string OutputFilePath { get; }
        public char Delimiter { get; }
        public string DebCode { get; set; }

        public Config(IDictionary<string, object> parameters)
        {
            InputFilePath = DictProcessor.ExtractRequiredString(parameters, Parameters.InputFilePath);
            OutputFilePath = DictProcessor.ExtractRequiredString(parameters, Parameters.OutputFilePath);

            Delimiter = DictProcessor.ExtractDelimiter(parameters, Parameters.Delimiter);
        }
    }
    internal class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 3)
                throw new ArgumentException(
                    "Ожидаются 3 аргумента: [0] путь к входному CSV-файлу, [1] путь к выходному CSV-файлу, [2] разделитель");
            IDictionary<string, object> parameters = new Dictionary<string, object>()
            {
                { Parameters.InputFilePath, args[0] },
                { Parameters.OutputFilePath, args[1] },
                { Parameters.Delimiter, args[2] }
            };

            var result = Execute(parameters);

            Console.WriteLine(SerializationHelper.DictionaryToJson(result));
        }

        public static IDictionary<string, object> Execute(IDictionary<string, object> parameters)
        {
            var resultDict = new Dictionary<string, object>();
            var engine = new CsvProcessorEngine();
            var config = new Config(parameters);

            var table = engine.ReadCsv(config.InputFilePath, config.Delimiter);

            config.DebCode = table.GetCommonValue(1);

            // 4.4.4 - 1
            table.NormalizeNumbers(4);
            table.NormalizeNumbers(7);
            table.NormalizeNumbers(8);

            engine.WriteCsv(table, config.OutputFilePath, config.Delimiter);

            // 4.4.4 - 2
            table.NormalizeDates(3);
            table.NormalizeDates(6);

            engine.WriteCsv(table, config.OutputFilePath, config.Delimiter);

            // 4.4.4 - 3
            for (int i = 0; i < table.Rows.Count; i++)
            {
                if (table.GetCellValueAsDouble(i, 7) == 0 && table.GetCellValueAsDouble(i, 8) != 0)
                {
                    table.SetCellValue(i, 7, table.GetCellValueAsDouble(i, 8).ToString("F2", new CultureInfo("ru-RU")));
                }
            }

            // 4.4.4 - 4
            var dupes = table.FindDuplicateRows(5);
            if (dupes.Count > 0)
            {
                List<int> sumColumnIndexes = new List<int>() { 7, 8 };
                foreach (var dupeValue in dupes.Keys)
                {
                    foreach (var columnIndex in sumColumnIndexes)
                    {
                        var sum = table.SumValues(columnIndex, dupes[dupeValue]);
                        table.SetCellValue(dupes[dupeValue][0], columnIndex, sum.ToString("F2", new CultureInfo("ru-RU")));
                    }
                    table.DeleteRows(dupes[dupeValue].Skip(1).ToList());
                }
            }

            engine.WriteCsv(table, config.OutputFilePath, config.Delimiter);

            // 4.4.4 - 5
            table.SubtractWithCheck(7);

            engine.WriteCsv(table, config.OutputFilePath, config.Delimiter);


            // 4.4.4 - 6
            for (int i = 0; i < table.Rows.Count; i++)
            {
                double totalSum = table.GetCellValueAsDouble(i, 7);
                double totalOst = table.GetCellValueAsDouble(i, 8);
                if (totalOst > totalSum)
                {
                    table.SetCellValue(i, 8, totalSum.ToString("F2", new CultureInfo("ru-RU")));
                }
            }

            engine.WriteCsv(table, config.OutputFilePath, config.Delimiter);
            return resultDict;
        }
    }
}
