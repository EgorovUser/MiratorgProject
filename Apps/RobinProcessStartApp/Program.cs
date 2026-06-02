using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;

namespace RobinProcessStartApp
{
    /// <summary>Имена параметров, передаваемых через IDictionary</summary>
    public static class ParametersName
    {
        public const string IsCMD = "isCMD";
        public const string InputExe = "inputExe";

        // 8 отдельных аргументов
        public const string ExeArg1 = "exeArg1";
        public const string ExeArg2 = "exeArg2";
        public const string ExeArg3 = "exeArg3";
        public const string ExeArg4 = "exeArg4";
        public const string ExeArg5 = "exeArg5";
        public const string ExeArg6 = "exeArg6";
        public const string ExeArg7 = "exeArg7";
        public const string ExeArg8 = "exeArg8";

        public const string ResultJson = "resultJson";
        public const string ResultPath = "path";
    }

    internal class Program
    {
        static void Main(string[] args)
        {
            IDictionary<string, object> parameters = new Dictionary<string, object>()
            {
                { ParametersName.IsCMD, args[0] },
                { ParametersName.InputExe, args[1] },
                { ParametersName.ExeArg1, args[2] },
                { ParametersName.ExeArg2, args[3] },
                { ParametersName.ExeArg3, args[4] },
                { ParametersName.ExeArg4, args[5] },
                { ParametersName.ExeArg5, args[6] },
                { ParametersName.ExeArg6, args[7] },
                { ParametersName.ExeArg7, args[8] },
                { ParametersName.ExeArg8, args[9] }
            };
            var result = Execute(parameters);
            foreach (var param in result)
            {
                Console.WriteLine($"{param.Key}: {param.Value}");
            }
        }

        public static IDictionary<string, object> Execute(IDictionary<string, object> parameters)
        {
            var resultDict = new Dictionary<string, object>
            {
                { ParametersName.ResultJson, string.Empty },
                { ParametersName.ResultPath, string.Empty }
            };

            bool useCmd = ParseBoolParameter(parameters, ParametersName.IsCMD, defaultValue: true);

            if (!TryGetStringParameter(parameters, ParametersName.InputExe, out string appFilePath))
                throw new ArgumentException($"В словаре отсутствует ключ '{ParametersName.InputExe}' или его значение является пустой строкой.");

            if (!File.Exists(appFilePath))
                throw new FileNotFoundException("Файл приложения не найден.", appFilePath);

            // Собираем аргументы из 8 параметров
            string argsStr = BuildArgumentsString(parameters);

            ProcessStartInfo psi = useCmd
                ? BuildCmdStartInfo(appFilePath, argsStr)
                : BuildPowerShellStartInfo(appFilePath, argsStr);

            RunProcessAndCaptureOutput(psi, appFilePath, out string output, out string error, out int exitCode);

            if (!string.IsNullOrWhiteSpace(error))
                throw new Exception(error.Trim());

            if (exitCode != 0 && string.IsNullOrWhiteSpace(output))
                throw new Exception($"Процесс завершился с кодом ошибки: {exitCode}");

            if (!string.IsNullOrWhiteSpace(output))
                ProcessOutput(output, appFilePath, resultDict);

            return resultDict;
        }

        #region Arguments Building

        private static string BuildArgumentsString(IDictionary<string, object> parameters)
        {
            var argKeys = new[]
            {
                ParametersName.ExeArg1, ParametersName.ExeArg2, ParametersName.ExeArg3, ParametersName.ExeArg4,
                ParametersName.ExeArg5, ParametersName.ExeArg6, ParametersName.ExeArg7, ParametersName.ExeArg8
            };

            var argsList = new List<string>();
            foreach (var key in argKeys)
            {
                if (TryGetStringParameter(parameters, key, out string arg))
                    argsList.Add(QuoteIfNeeded(arg)); // Оборачиваем в кавычки при необходимости
            }

            if (argsList.Count > 0)
                return string.Join(" ", argsList);

            return string.Empty;
        }

        /// <summary>
        /// Оборачивает аргумент в кавычки, если он содержит пробелы и еще не обернут.
        /// </summary>
        private static string QuoteIfNeeded(string arg)
        {
            if (arg.Contains(" ") && !arg.StartsWith("\"") && !arg.EndsWith("\""))
            {
                return $"\"{arg}\"";
            }
            return arg;
        }

        #endregion

        #region Process Start Info Builders

        private static ProcessStartInfo BuildCmdStartInfo(string appFilePath, string argsStr)
        {
            // Путь к exe также оборачиваем в кавычки на случай пробелов
            string arguments = string.IsNullOrWhiteSpace(argsStr)
                ? $"/c \"\"{appFilePath}\"\""
                : $"/c \"\"{appFilePath}\" {argsStr}\"";

            return new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
                // Кодировка не указывается намеренно — используется системная по умолчанию
            };
        }

        private static ProcessStartInfo BuildPowerShellStartInfo(string appFilePath, string argsStr)
        {
            string escapedPath = appFilePath.Replace("'", "''");

            // Для корректной передачи кавычек внутри строки PowerShell экранируем их \"
            string escapedArgs = argsStr.Replace("\"", "\\\"");

            string arguments = string.IsNullOrWhiteSpace(escapedArgs)
                ? $"-NoProfile -NonInteractive -Command \"& '{escapedPath}'; exit $LASTEXITCODE\""
                : $"-NoProfile -NonInteractive -Command \"& '{escapedPath}' {escapedArgs}; exit $LASTEXITCODE\"";

            return new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }

        #endregion

        #region Process Execution

        private static void RunProcessAndCaptureOutput(
            ProcessStartInfo psi,
            string appFilePath,
            out string output,
            out string error,
            out int exitCode)
        {
            using (var process = Process.Start(psi))
            {
                if (process == null)
                    throw new InvalidOperationException($"Не удалось запустить {appFilePath}");

                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();

                process.OutputDataReceived += (s, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                process.WaitForExit();

                output = outputBuilder.ToString().TrimEnd('\r', '\n');
                error = errorBuilder.ToString().TrimEnd('\r', '\n');
                exitCode = process.ExitCode;
            }
        }

        #endregion

        #region Output Processing

        private static void ProcessOutput(string output, string appFilePath, Dictionary<string, object> resultDict)
        {
            const string resultPrefix = "result: ";
            string trimmedOutput = output.Trim();

            if (trimmedOutput.StartsWith(resultPrefix, StringComparison.OrdinalIgnoreCase))
            {
                // Как раньше — отрезаем префикс
                string resultValue = trimmedOutput.Substring(resultPrefix.Length);
                AssignResult(resultValue, resultDict);
            }
            else if (LooksLikeJson(trimmedOutput))
            {
                // JSON пришёл без префикса — всё равно считаем успешным результатом
                AssignResult(trimmedOutput, resultDict);
            }
            else
            {
                throw new Exception($"Ошибка выполнения {appFilePath}: {trimmedOutput}");
            }
        }

        /// <summary>Проверяет, похожа ли строка на JSON-объект или массив</summary>
        private static bool LooksLikeJson(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            char first = s.TrimStart()[0];
            return first == '{' || first == '[';
        }

        /// <summary>Разбор значения и заполнение словаря</summary>
        private static void AssignResult(string resultValue, Dictionary<string, object> resultDict)
        {
            Console.WriteLine(resultValue);

            if (TryExtractSingleElement(resultValue, out string singleValue))
                resultDict[ParametersName.ResultPath] = singleValue;

            resultDict[ParametersName.ResultJson] = resultValue;
        }

        private static bool TryExtractSingleElement(string resultValue, out string singleValue)
        {
            singleValue = resultValue;
            if (string.IsNullOrWhiteSpace(resultValue)) return false;

            string trimmed = resultValue.Trim();

            // 1. Если это JSON-массив [...]
            if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
            {
                try
                {
                    var bytes = Encoding.UTF8.GetBytes(trimmed);
                    using (var stream = new MemoryStream(bytes))
                    {
                        var serializer = new DataContractJsonSerializer(typeof(string[]));
                        var arr = serializer.ReadObject(stream) as string[];

                        // Если в массиве 1 элемент и он является путём
                        if (arr != null && arr.Length == 1 && IsPathLike(arr[0]))
                        {
                            singleValue = arr[0];
                            return true;
                        }
                        return false;
                    }
                }
                catch
                {
                    return false;
                }
            }

            // 2. Если это JSON-объект {...}
            if (trimmed.StartsWith("{") && trimmed.EndsWith("}"))
            {
                try
                {
                    var bytes = Encoding.UTF8.GetBytes(trimmed);
                    using (var stream = new MemoryStream(bytes))
                    {
                        // DataContractJsonSerializer умеет десериализовать объекты в Dictionary
                        var serializer = new DataContractJsonSerializer(typeof(Dictionary<string, string>));
                        var dict = serializer.ReadObject(stream) as Dictionary<string, string>;

                        // Проверяем: ровно 1 пара и значение является путём
                        if (dict != null && dict.Count == 1)
                        {
                            var kvp = dict.First();
                            if (IsPathLike(kvp.Value))
                            {
                                singleValue = kvp.Value;
                                return true;
                            }
                        }
                        // Если пар больше одной или значение не путь — возвращаем false
                        // и JSON целиком пойдет в ResultJson
                        return false;
                    }
                }
                catch
                {
                    // Если JSON сложной структуры (вложенные объекты), десериализация в 
                    // Dictionary<string, string> упадет, и мы уйдем в catch — это нормально,
                    // значит это точно не 1 пара ключ-значение
                    return false;
                }
            }

            // 3. Если это просто строка в кавычках "..."
            if (trimmed.StartsWith("\"") && trimmed.EndsWith("\""))
            {
                try
                {
                    var bytes = Encoding.UTF8.GetBytes(trimmed);
                    using (var stream = new MemoryStream(bytes))
                    {
                        var serializer = new DataContractJsonSerializer(typeof(string));
                        var str = serializer.ReadObject(stream) as string;

                        if (str != null && IsPathLike(str))
                        {
                            singleValue = str;
                            return true;
                        }
                    }
                }
                catch { }
            }

            // Во всех остальных случаях (включая обычный текст без кавычек) 
            // считаем, что это не единичный путь
            return false;
        }

        /// <summary>
        /// Проверяет, похожа ли строка на путь к файлу или папке.
        /// Существование файла/папки на диске НЕ проверяется.
        /// </summary>
        private static bool IsPathLike(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;

            // Отсекаем строки, содержащие недопустимые символы пути
            if (value.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return false;

            // Абсолютный путь (C:\, D:/, \\server\share)
            if (Path.IsPathRooted(value)) return true;

            // Относительный путь (содержит слэши, например folder\file.txt)
            if (value.Contains("\\") || value.Contains("/")) return true;

            return false;
        }

        #endregion

        #region Parameter Helpers

        private static bool ParseBoolParameter(IDictionary<string, object> parameters, string key, bool defaultValue)
        {
            if (!parameters.TryGetValue(key, out var value) || value == null)
                return defaultValue;

            switch (value)
            {
                case bool b: return b;
                case string s when bool.TryParse(s, out bool parsed): return parsed;
                case string s: return s.Equals("1", StringComparison.Ordinal) || s.Equals("true", StringComparison.OrdinalIgnoreCase);
                default: return defaultValue;
            }
        }

        private static bool TryGetStringParameter(IDictionary<string, object> parameters, string key, out string value)
        {
            value = null;
            if (!parameters.TryGetValue(key, out var obj) || !(obj is string s) || string.IsNullOrWhiteSpace(s))
                return false;

            value = s;
            return true;
        }

        #endregion
    }
}