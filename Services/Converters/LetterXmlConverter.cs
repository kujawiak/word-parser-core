using System.Xml.Linq;
using ModelDto.EditorialUnits;

namespace WordParserCore.Services.Converters
{
    /// <summary>
    /// Konwerter dla liter - transformuje LetterDto do XML.
    /// </summary>
    public class LetterXmlConverter
    {
        // public XElement ToXml(LetterDto letter, bool generateGuids = false)
        // {
        //     var newElement = new XElement(XmlConstants.Letter,
        //         new XAttribute("id", letter.Id));
        //     if (generateGuids) newElement.Add(new XAttribute("guid", letter.Guid));
        //     newElement.AddFirst(new XElement(XmlConstants.Number, letter.Number?.Value ?? string.Empty));
        //     newElement.Add(new XElement("text", letter.ContentText));

        //     foreach (var tiret in letter.Tirets)
        //     {
        //         var tiretConverter = new TiretXmlConverter();
        //         newElement.Add(tiretConverter.ToXml(tiret, generateGuids));
        //     }

        //     // TODO: Dodać Amendment conversion
        //     foreach (var amendment in letter.Amendments)
        //     {
        //         // newElement.Add(amendment.ToXML(generateGuids));
        //     }

        //     return newElement;
        // }
    }
}
