using System.Xml.Linq;
using ModelDto.EditorialUnits;

namespace WordParserCore.Services.Converters
{
    /// <summary>
    /// Konwerter dla punktów - transformuje PointDto do XML.
    /// </summary>
    public class PointXmlConverter
    {
        // public XElement ToXml(PointDto point, bool generateGuids = false)
        // {
        //     var newElement = new XElement(XmlConstants.Point,
        //         new XAttribute("id", point.Id));
        //     if (generateGuids) newElement.Add(new XAttribute("guid", point.Guid));
        //     newElement.AddFirst(new XElement(XmlConstants.Number, point.Number));
        //     newElement.Add(new XElement("text", point.ContentText));

        //     foreach (var letter in point.Letters)
        //     {
        //         var letterConverter = new LetterXmlConverter();
        //         newElement.Add(letterConverter.ToXml(letter, generateGuids));
        //     }

        //     // TODO: Dodać Amendment conversion
        //     foreach (var amendment in point.Amendments)
        //     {
        //         // newElement.Add(amendment.ToXML(generateGuids));
        //     }

        //     return newElement;
        // }
    }
}
