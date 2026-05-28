using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using System.Web.Script.Serialization;

namespace CommonLib
{
    /// <summary>
    /// Утилиты для сериализации объектов в XML и JSON
    /// с помощью стандартных классов .NET Framework 4.8.
    /// </summary>
    public static class SerializationHelper
    {

        /// <summary>
        /// Сериализует объект в XML-строку с помощью XmlSerializer.
        /// </summary>
        /// <typeparam name="T">Тип сериализуемого объекта</typeparam>
        /// <param name="obj">Объект для сериализации</param>
        /// <param name="indent">Форматировать с отступами</param>
        /// <param name="omitXmlDeclaration">Пропустить XML-декларацию</param>
        /// <returns>XML-строка</returns>
        public static string ToXml<T>(T obj, bool indent = true, bool omitXmlDeclaration = false)
        {
            var serializer = new XmlSerializer(typeof(T));

            var settings = new XmlWriterSettings
            {
                Indent = indent,
                Encoding = Encoding.UTF8,
                OmitXmlDeclaration = omitXmlDeclaration
            };

            using (var memoryStream = new MemoryStream())
            using (var writer = XmlWriter.Create(memoryStream, settings))
            {
                // Пустое пространство имён — убираем лишние xmlns
                var ns = new XmlSerializerNamespaces();
                ns.Add("", "");

                serializer.Serialize(writer, obj, ns);

                return Encoding.UTF8.GetString(memoryStream.ToArray());
            }
        }

        /// <summary>
        /// Сохраняет объект в XML-файл.
        /// </summary>
        /// <typeparam name="T">Тип сериализуемого объекта</typeparam>
        /// <param name="obj">Объект для сериализации</param>
        /// <param name="filePath">Путь к выходному файлу</param>
        /// <param name="indent">Форматировать с отступами</param>
        public static void SaveXml<T>(T obj, string filePath, bool indent = true)
        {
            string xml = ToXml(obj, indent);
            File.WriteAllText(filePath, xml, Encoding.UTF8);
        }

        public static string DictionaryToJson(IDictionary<string, object> obj)
        {
            var serializer = new JavaScriptSerializer();
            return serializer.Serialize(obj);
        }
    }
}
