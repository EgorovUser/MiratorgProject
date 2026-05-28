using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.Serialization.Json;
using TxtTableParsing;
using CsvParsing;
using static DataSearchAlgorithmApp.SapAlvConfig;
using CommonLib;

namespace DataSearchAlgorithmApp
{
    /// <summary>Имена параметров, передаваемых через IDictionary</summary>
    public static class Parameters
    {
        public const string DebtorCode = "debtorCode";
        public const string ScriptPath = "scriptPath";
        public const string TempDir = "tempDir";
        public const string ParseVariant = "parseVariant";
        public const string InputCsv = "inputCsv";
        public const string Delimiter = "delimiter";

        public const string ResultBE = "BE";
        public const string ResultDocumentDate = "DocumentDate";
        public const string ResultDocumentNumber = "DocumentNumber";
        public const string ResultDocumentSum = "DocumentSum";
        public const string ResultDocumentPP = "DocumentPP";
        public const string ResultDocumentDatePP = "DocumentDatePP";
        public const string ResultDocuments = "DocumentsDict";
        public const string ResultCsvFile = "ResulCsvFile";
    }

    /// <summary>
    /// Конфигурация парсера SAP ALV (txt с табуляторами).
    /// </summary>
    public static class SapAlvConfig
    {
        /// <remarks>
        /// Имена столбцов соответствуют заголовкам из SAP-выгрузки
        /// «Текст с табуляторами» (Report → List → Export → Spreadsheet).
        /// </remarks>
        public static class ColumnsCommon
        {
            public const string BEValue = "Знач. БЕ";
            public const string DocumentText = "Текст";
            public const string DocumentValue = "ЗначВалДок";
            public const string DocumentDate = "Д/докум.";
        }
        public static class ColumnsV2
        {
            public const string DocumentDate = "ДатаДокум";
            public const string DocumentType = "Вид";
            public const string DocumentNumber = "№ докум.";
            public const string DocumentLink = "Ссылка";
            public const string DocumentSF = "Счет-факт";
        }
        public static class Collumns62PP
        {
            public const string DocumentDate = "Д/проводки";
            public const string DocumentNumber = "№ докум.";
        }

        public static readonly string[] RequiredHeaderColumns =
        {
            ColumnsCommon.BEValue,
        };

        public static ParserOptions CreateParserOptions()
        {
            return new ParserOptions
            {
                Delimiter = '\t',
                RequiredColumns = RequiredHeaderColumns,
                MaxHeaderSearchRows = 20
            };
        }
    }


    internal class Program
    {
        static string debugString = "";
        static void Main(string[] args)
        {
            if (args.Length < 6)
                throw new ArgumentException("Ожидаются 6 аргументов: [0] код дебитора, [1] путь к скрипту, [2] временная папка, [3] вариант парсинга, [4] входной CSV, [5] разделитель.");
            IDictionary<string, object> parameters = new Dictionary<string, object>()
            {
                { Parameters.DebtorCode, args[0] },
                { Parameters.ScriptPath, args[1] },
                { Parameters.TempDir, args[2] },
                { Parameters.ParseVariant, args[3] },
                { Parameters.InputCsv, args[4] },
                { Parameters.Delimiter, args[5] }
            };
            var result = Execute(parameters);

            Console.WriteLine(SerializationHelper.DictionaryToJson(result));
        }
        public static IDictionary<string, object> Execute(IDictionary<string, object> parameters)
        {

            IDictionary<string, object> resultDict = new Dictionary<string, object>
            {
                { Parameters.ResultBE, "" },
                { Parameters.ResultDocumentDate, "" },
                { Parameters.ResultDocumentNumber, "" },
                { Parameters.ResultDocumentSum, "" },
                { Parameters.ResultDocumentPP, "" },
                { Parameters.ResultDocumentDatePP, "" },
                { Parameters.ResultDocuments, new Dictionary<string, object>() },
                { Parameters.ResultCsvFile, "" }
            };


            // ─── Извлечение параметров ──────────────────────────────────
            string scriptFilePath = DictProcessor.ExtractRequiredString(parameters, Parameters.ScriptPath);
            string tempDir = DictProcessor.ExtractRequiredString(parameters, Parameters.TempDir);
            string parseVariant = DictProcessor.ExtractRequiredString(parameters, Parameters.ParseVariant);
            string debtorCode = DictProcessor.ExtractRequiredString(parameters, Parameters.DebtorCode);
            string inputCsvPath = DictProcessor.ExtractRequiredString(parameters, Parameters.InputCsv);
            char csvDelimiter = DictProcessor.ExtractDelimiter(parameters, Parameters.Delimiter);

            if (!File.Exists(scriptFilePath))
                throw new Exception($"Файл wsf не найден: {scriptFilePath}");
            if (!Directory.Exists(tempDir))
                throw new Exception($"Папка не найдена: {tempDir}");
            if (!File.Exists(inputCsvPath))
                throw new Exception($"Файл csv не найден: {inputCsvPath}");

            string keyDateFull = DateTime.Now.ToString("dd.MM.yyyy_HH-mm-ss");
            string tempFileName = $"FBL5H_{parseVariant}_{keyDateFull}.txt";
            string tempFilePath = Path.Combine(tempDir, tempFileName);
            string errorFileName = $"FBL5H_ERROR_{keyDateFull}.txt";
            string errorFilePath = Path.Combine(tempDir, errorFileName);
            string sapForm = "";
            var csvEngine = new CsvProcessorEngine();
            var csvTable = csvEngine.ReadCsv(inputCsvPath, csvDelimiter);

            switch (parseVariant)
            {
                case "SumAndYear":
                    sapForm = "/62+№ПП";
                    break;
                case "DateAndSummSF":
                    sapForm = "/V2";
                    break;
                case "SumAndDate":
                    break;
                case "DateAndNumber":
                    break;
                default:
                    throw new Exception($"Неизвестный вариант - \"{parseVariant}\"");
            }
            /*
            var psi = new ProcessStartInfo();
            psi.FileName = "cscript.exe";
            psi.Arguments = $"//NoLogo \"{scriptFilePath}\" \"{tempDir}\" \"{tempFileName}\" \"{errorFileName}\" \"{debtorCode}\" \"{sapForm}\"";
            psi.WorkingDirectory = Path.GetDirectoryName(scriptFilePath);
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;

            using (var process = Process.Start(psi))
            {
                // Обязательно читаем оба потока до WaitForExit!
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();

                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    // Собираем максимум информации об ошибке
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine($"Скрипт завершился с кодом ошибки {process.ExitCode}.");

                    if (!string.IsNullOrWhiteSpace(error))
                        sb.AppendLine($"[STDERR]: {error}");

                    // VBScript пишет свои ошибки именно в STDOUT!
                    if (!string.IsNullOrWhiteSpace(output))
                        sb.AppendLine($"[STDOUT]: {output}");

                    // Если скрипт успел создать файл ошибки до того как упал, читаем его
                    if (File.Exists(errorFilePath))
                    {
                        string errorFileText = FileReader.RewriteAndReadFileToUTF8(errorFilePath);
                        if (!string.IsNullOrWhiteSpace(errorFileText))
                            sb.AppendLine($"[ERROR FILE]: {errorFileText}");
                    }

                    throw new Exception(sb.ToString());
                }
            }
            
            string fileText = FileReader.RewriteAndReadFileToUTF8(tempFilePath, false);
            if (string.IsNullOrWhiteSpace(fileText))
            {
                string errorFileText = FileReader.RewriteAndReadFileToUTF8(errorFilePath);

                if (string.IsNullOrWhiteSpace(errorFileText))
                {
                    throw new Exception($"Результирующий файл пуст, и файл ошибки также отсутствует. Путь к файлу результата: {tempFilePath}");
                }

                throw new Exception(errorFileText);
            }
            */
            try
            {
                // 1. Парсинг файла
                var parser = new TabDelimitedParser("C:\\Users\\AAEgorov\\source\\repos\\MiratorgProject\\Apps\\Common\\DataSearchAlgorithmApp\\bin\\Debug\\FBL5H_SumAndYear_19.05.2026_08-18-13.txt", CreateParserOptions());
                ParsedTable table = parser.Parse();

                // 2. Бизнес-логика (фильтрация, группировка)
                resultDict = ProcessTxt(table, resultDict, parseVariant, csvTable);

            }
            catch (Exception ex)
            {
                throw new Exception(
                    $"Ошибка при обработке: {ex.InnerException?.Message ?? ex.Message}\n{ex.StackTrace}\nDebugString - {debugString}");
            }

            csvEngine.WriteCsv(csvTable, inputCsvPath, csvDelimiter);
            resultDict[Parameters.ResultCsvFile] = inputCsvPath;

            return resultDict;
        }

        public static IDictionary<string, object> ProcessTxt(ParsedTable table, IDictionary<string, object> parameters, string parseVariant, CsvTable csvTable)
        {
            debugString += $"START ";
            string searchValue = string.Empty;
            debugString += $"READ";
            if (table == null || !table.HasData)
                throw new InvalidOperationException("Нет данных для обработки.");
            debugString += $"NOT NULL";
            if (parseVariant == "SumAndYear")
            {
                debugString += $"SumAndYear";
                searchValue = csvTable.Rows[0][2];
                var filtered = table.Where(r =>
                    r.EqualsValue(Collumns62PP.DocumentNumber, searchValue)).ToList();
                Row firstRow = filtered.First();
                parameters[Parameters.ResultBE] = firstRow[ColumnsCommon.BEValue];
                parameters[Parameters.ResultDocumentDate] = firstRow[Collumns62PP.DocumentDate];
                parameters[Parameters.ResultDocumentNumber] = firstRow[Collumns62PP.DocumentNumber];
                csvTable.Rows[0][3] = firstRow[Collumns62PP.DocumentDate];
                csvTable.Rows[0][4] = firstRow[ColumnsCommon.BEValue];
                debugString += $"SAY END";
            }

            if (parseVariant == "DateAndSummSF")
            {
                debugString += $"DateAndSummSF";
                debugString += $"READ";
                Dictionary<string, object> keyValuePairs = new Dictionary<string, object>();
                debugString += $"NEW";

                foreach (var row in csvTable.Rows)
                {
                    searchValue = row[5];
                    debugString += $"searchValue {searchValue}";
                    Dictionary<string, object> innerDict = new Dictionary<string, object>();
                    debugString += $"InnerNew";
                    var filteredByDocLink = table.Where(r =>
                        r.EqualsValue(ColumnsV2.DocumentLink, searchValue)).ToList();
                    if (filteredByDocLink.Count == 0) continue;
                    debugString += $"AfFiter";
                    var filteredBySF = table.Where(r =>
                        r.EqualsValue(ColumnsV2.DocumentSF, filteredByDocLink[0][ColumnsV2.DocumentSF])).ToList();

                    decimal sum = 0;
                    foreach (var filteredRow in filteredBySF)
                    {
                        DateHelper.TryParseDecimal(filteredRow[ColumnsCommon.BEValue], out decimal amount);
                        sum += amount;
                    }
                    innerDict[Parameters.ResultDocumentSum] = DateHelper.ToStringDecimal(sum);

                    var filteredFromCL = filteredBySF.Where(r =>
                            r.EqualsValue(ColumnsV2.DocumentType, "RV")).ToList();
                    debugString += $"Add";
                    Row firstRow = filteredFromCL[0];
                    innerDict[Parameters.ResultDocumentNumber] = firstRow[ColumnsV2.DocumentNumber];
                    var year = DateTime.ParseExact(firstRow[ColumnsV2.DocumentDate], "dd.MM.yyyy", null).Year.ToString();
                    innerDict[Parameters.ResultDocumentDate] = year;
                    keyValuePairs[searchValue] = innerDict;
                    row[5] = firstRow[ColumnsV2.DocumentNumber];
                    row[6] = year;
                    row[8] = DateHelper.ToStringDecimal(sum);

                    debugString += $"ADD PAIR";
                }
                debugString += $"END FOR";
                parameters[Parameters.ResultDocuments] = keyValuePairs;
                debugString += $"DAS END";
            }

            if (parseVariant == "SumAndDate")
            {
                debugString += $"SumAndDate";
                searchValue = csvTable.Rows[0][2];
                var filtered = table
                    .Where(r => r.ContainsWord(ColumnsCommon.DocumentText, searchValue))
                    .ToList();
                if (filtered.Count == 0)
                {
                    filtered = table.Where(r =>
                        r.ContainsValue(ColumnsCommon.DocumentText, searchValue)).ToList();
                }
                Row firstRow = ParsedTable.OrderByDateTakeFirst(filtered, ColumnsCommon.DocumentDate);
                parameters[Parameters.ResultDocumentSum] = firstRow[ColumnsCommon.DocumentValue];
                parameters[Parameters.ResultDocumentDate] = firstRow[ColumnsCommon.DocumentDate];
                csvTable.Rows[0][3] = firstRow[ColumnsCommon.DocumentDate];
                csvTable.Rows[0][4] = firstRow[ColumnsCommon.DocumentValue];
                debugString += $"SAD END";
            }

            if (parseVariant == "DateAndNumber")
            {
                debugString += $"DateAndNumber";
                searchValue = csvTable.Rows[0][4];
                DateHelper.TryParseDecimal(searchValue, out decimal searchValueDecmal);
                string newSearchValue = DateHelper.ToStringDecimal(searchValueDecmal);
                var filtered = table.Where(r =>
                    r.EqualsValue(ColumnsCommon.DocumentValue, newSearchValue)).ToList();
                if (filtered.Count == 0)
                    filtered = table.Where(r =>
                        r.EqualsValue(ColumnsCommon.DocumentValue, newSearchValue.Replace(" ", ""))).ToList();
                Row firstRow = ParsedTable.OrderByDateTakeFirst(filtered, ColumnsCommon.DocumentDate);
                parameters[Parameters.ResultDocumentPP] = firstRow[ColumnsCommon.DocumentText];
                parameters[Parameters.ResultDocumentDatePP] = firstRow[ColumnsCommon.DocumentDate];
                csvTable.Rows[0][2] = firstRow[ColumnsCommon.DocumentText];
                csvTable.Rows[0][3] = firstRow[ColumnsCommon.DocumentDate];
                debugString += $"DAN END";
            }
            debugString += $"RET";
            return parameters;
        }
    }
}
