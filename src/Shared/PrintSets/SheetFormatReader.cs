using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace PallMall
{
    /// <summary>
    /// Читает формат листа с основной надписи: «Формат А» (1 = А1) и «Кратность».
    ///
    /// Параметры могут жить как на экземпляре надписи, так и на её типе,
    /// поэтому ищем в обоих местах. Если точного имени нет — берём первый
    /// числовой параметр с подходящим словом в названии.
    /// </summary>
    internal static class SheetFormatReader
    {
        /// <summary>Собирает основные надписи модели, разложенные по листам.</summary>
        internal static Dictionary<ElementId, List<FamilyInstance>> CollectBySheet(Document doc)
        {
            var map = new Dictionary<ElementId, List<FamilyInstance>>();

            foreach (FamilyInstance titleBlock in new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>())
            {
                ElementId owner = titleBlock.OwnerViewId;
                if (owner == null || owner == ElementId.InvalidElementId)
                    continue;

                List<FamilyInstance> list;
                if (!map.TryGetValue(owner, out list))
                {
                    list = new List<FamilyInstance>();
                    map.Add(owner, list);
                }

                list.Add(titleBlock);
            }

            return map;
        }

        /// <summary>
        /// Подпись формата по надписям одного листа. null — определить не удалось,
        /// причина при этом попадает в problem.
        /// </summary>
        internal static string Read(
            IList<FamilyInstance> titleBlocks,
            out string problem,
            out bool multipleTitleBlocks)
        {
            problem = null;
            multipleTitleBlocks = false;

            if (titleBlocks == null || titleBlocks.Count == 0)
            {
                problem = "нет основной надписи";
                return null;
            }

            multipleTitleBlocks = titleBlocks.Count > 1;
            FamilyInstance titleBlock = titleBlocks[0];

            int format;
            if (!TryReadNumber(titleBlock, "Формат А", "формат", out format) || format < 0)
            {
                problem = "нет параметра «Формат А»";
                return null;
            }

            int multiplicity;
            if (!TryReadNumber(titleBlock, "Кратность", "кратност", out multiplicity) || multiplicity < 1)
                multiplicity = 1;

            return PrintSetNaming.FormatLabel(format, multiplicity);
        }

        private static bool TryReadNumber(
            FamilyInstance titleBlock,
            string exactName,
            string keyword,
            out int value)
        {
            Parameter parameter = titleBlock.LookupParameter(exactName);
            if (TryGetNumber(parameter, out value))
                return true;

            FamilySymbol symbol = titleBlock.Symbol;
            if (symbol != null)
            {
                parameter = symbol.LookupParameter(exactName);
                if (TryGetNumber(parameter, out value))
                    return true;
            }

            parameter = FindNumericParameter(titleBlock, keyword);
            if (TryGetNumber(parameter, out value))
                return true;

            if (symbol != null)
            {
                parameter = FindNumericParameter(symbol, keyword);
                if (TryGetNumber(parameter, out value))
                    return true;
            }

            value = 0;
            return false;
        }

        private static Parameter FindNumericParameter(Element element, string keyword)
        {
            foreach (Parameter parameter in element.Parameters)
            {
                if (parameter == null || parameter.StorageType != StorageType.Integer)
                    continue;

                Definition definition = parameter.Definition;
                if (definition == null)
                    continue;

                if (definition.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    return parameter;
            }

            return null;
        }

        private static bool TryGetNumber(Parameter parameter, out int value)
        {
            value = 0;

            if (parameter == null || !parameter.HasValue)
                return false;

            if (parameter.StorageType == StorageType.Integer)
            {
                value = parameter.AsInteger();
                return true;
            }

            if (parameter.StorageType == StorageType.String)
                return int.TryParse(parameter.AsString(), out value);

            return false;
        }
    }
}
