using CommonLib;
using CsvParsing;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using XmlParser;


namespace FindAndFixErrors7App
{
    /// <summary>Имена параметров, передаваемых через IDictionary</summary>
    public static class Parameters
    {
        public const string InputFilePath = "inputFilePath";
        public const string InputXmlFilePath = "inputXmlFilePath";
        public const string Delimiter = "delimiter";
        public const string OutputDir = "outputDir";
        public const string ExcludeCurrentMonth = "excludeCurrentMonth";

        public const string NewDate = "newDate";
    }
    internal class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 4)
                throw new ArgumentException(
                    "Ожидаются до 5 аргументов: [0] путь к входному CSV-файлу, [1] путь к выходному CSV-файлу, [2] разделитель, [3] путь к входному XML-файлу, [4] исключить текущий месяц (true/false) (опционально).");
            IDictionary<string, object> parameters = new Dictionary<string, object>()
            {
                { Parameters.InputFilePath, args[0] },
                { Parameters.OutputDir, args[1] },
                { Parameters.Delimiter, args[2] },
                { Parameters.InputXmlFilePath, args[3] }
            };
            if (args.Length > 4)
                parameters[Parameters.ExcludeCurrentMonth] = args[4];

            var result = Execute(parameters);

            Console.WriteLine(SerializationHelper.DictionaryToJson(result));
        }
        public static IDictionary<string, object> Execute(IDictionary<string, object> parameters)
        {
            var resultDict = new Dictionary<string, object>();
            var engine = new CsvProcessorEngine();

            string filePath = DictProcessor.ExtractRequiredString(parameters, Parameters.InputFilePath);
            string outputDir = DictProcessor.ExtractOptionalString(parameters, Parameters.OutputDir);
            string xmlFilePath = DictProcessor.ExtractRequiredString(parameters, Parameters.InputXmlFilePath);
            char delimiter = DictProcessor.ExtractDelimiter(parameters, Parameters.Delimiter);
            bool excludeCurrentMonth = DictProcessor.ExtractOptionalBool(parameters, Parameters.ExcludeCurrentMonth, false);

            string outputFilePath = Path.Combine(outputDir, Path.GetFileName(filePath));
            var table = engine.ReadCsv(filePath, delimiter);

            string xml = File.ReadAllText(xmlFilePath);
            List<XmlRow> xmlRows = XmlTableParser.Parse(xml);

            if (excludeCurrentMonth)
            {
                xmlRows = XmlTableParser.RemoveRowsWithCurrentMonth(xmlRows, "DocDate");
            }

            // 4.4.4 - 1
            table.NormalizeNumbers(4);
            table.NormalizeNumbers(7);
            table.NormalizeNumbers(8);

            // 4.4.4 - 2
            table.NormalizeDates(3);
            table.NormalizeDates(6);

            // 4.4.4 - 7 И
            if (!table.Comparison(xmlRows))
            {
                engine.WriteCsv(table, outputFilePath, delimiter);
                throw new Exception("Не получилось составить нужную сумму");
            }


            List<int> rowsToDelete = new List<int>();
            for (int i = 0; i < table.Rows.Count; i++)
            {
                if (table.GetCellValueAsDouble(i, 7) == 0)
                {
                    rowsToDelete.Add(i);
                }
            }
            table.DeleteRows(rowsToDelete);

            engine.WriteCsv(table, outputFilePath, delimiter);
            resultDict.Add("OutputFilePath", outputFilePath);
            return resultDict;
        }
    }
}
