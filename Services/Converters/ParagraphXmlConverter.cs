using System.Xml.Linq;
using ModelDto.EditorialUnits;

namespace WordParserCore.Services.Converters
{
    /// <summary>
    /// Konwerter dla ustępów - transformuje ParagraphDto do XML.
    /// </summary>
    public class ParagraphXmlConverter
    {
        // public XElement ToXml(ParagraphDto paragraph, bool generateGuids = false)
        // {
        //     var newElement = new XElement(XmlConstants.Subsection,
        //         new XAttribute("id", paragraph.Id));
        //     if (generateGuids) newElement.Add(new XAttribute("guid", paragraph.Guid));
        //     newElement.AddFirst(new XElement(XmlConstants.Number, paragraph.Number));
        //     newElement.Add(new XElement("text", paragraph.ContentText));

        //     foreach (var point in paragraph.Points)
        //     {
        //         var pointConverter = new PointXmlConverter();
        //         newElement.Add(pointConverter.ToXml(point, generateGuids));
        //     }

        //     // TODO: Dodać Amendment conversion
        //     foreach (var amendment in paragraph.Amendments)
        //     {
        //         // newElement.Add(amendment.ToXML(generateGuids));
        //     }

        //     return newElement;
        // }
    }
}
