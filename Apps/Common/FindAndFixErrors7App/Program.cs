using CsvParsing;
using System;
using System.Collections.Generic;
using System.IO;
using XmlParser;
using CommonLib;


namespace FindAndFixErrors7App
{
    /// <summary>Имена параметров, передаваемых через IDictionary</summary>
    public static class Parameters
    {
        public const string InputFilePath = "inputFilePath";
        public const string InputXmlFilePath = "inputXmlFilePath";
        public const string Delimiter = "delimiter";
        public const string OutputFilePath = "outputFilePath";
        public const string ExcludeCurrentMonth = "excludeCurrentMonth";

        public const string NewDate = "newDate";
    }
    internal class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 5)
                throw new ArgumentException(
                    "Ожидаются 5 аргументов: [0] путь к входному CSV-файлу, [1] путь к выходному CSV-файлу, [2] разделитель, [3] путь к входному XML-файлу, [4] исключить текущий месяц (true/false).");
            IDictionary<string, object> parameters = new Dictionary<string, object>()
            {
                { Parameters.InputFilePath, args[0] },
                { Parameters.OutputFilePath, args[1] },
                { Parameters.Delimiter, args[2] },
                { Parameters.InputXmlFilePath, args[3] },
                { Parameters.ExcludeCurrentMonth, args[4] }
            };

            var result = Execute(parameters);

            Console.WriteLine(SerializationHelper.DictionaryToJson(result));
        }
        public static IDictionary<string, object> Execute(IDictionary<string, object> parameters)
        {
            var resultDict = new Dictionary<string, object>();
            var engine = new CsvProcessorEngine();

            string filePath = DictProcessor.ExtractRequiredString(parameters, Parameters.InputFilePath);
            string outputFilePath = DictProcessor.ExtractOptionalString(parameters, Parameters.OutputFilePath);
            string xmlFilePath = DictProcessor.ExtractRequiredString(parameters, Parameters.InputXmlFilePath);
            char delimiter = DictProcessor.ExtractDelimiter(parameters, Parameters.Delimiter);
            bool excludeCurrentMonth = DictProcessor.ExtractOptionalBool(parameters, Parameters.ExcludeCurrentMonth, false);

            var table = engine.ReadCsv(filePath, delimiter);

            string xml = File.ReadAllText(xmlFilePath);
            List<XmlRow> xmlRows = XmlTableParser.Parse(xml);

            if (excludeCurrentMonth)
            {
                xmlRows = XmlTableParser.RemoveRowsWithCurrentMonth(xmlRows, "DocDate");
            }

            // 4.4.4 - 7 И
            if (!table.Comparison(xmlRows))
            {
                engine.WriteCsv(table, outputFilePath, delimiter);
                throw new Exception("Не получилось составить нужную сумму");
            }

            engine.WriteCsv(table, outputFilePath, delimiter);
            return resultDict;
        }
    }
}
