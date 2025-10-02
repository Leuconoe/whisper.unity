using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

public class TextNormalizer
{
    public string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // 1. Convertir a minúsculas
        text = text.ToLower();

        // 2. Eliminar acentos (opcional, pero recomendado para español)
        text = RemoveAccents(text);

        // 3. Eliminar caracteres especiales (excepto letras, números y espacios)
        text = Regex.Replace(text, @"[^a-zA-Z0-9\s]", "");

        // 4. Reemplazar múltiples espacios por uno solo
        text = Regex.Replace(text, @"\s+", " ");

        // 5. Recortar espacios al inicio y final
        text = text.Trim();

        return text;
    }

    // Método para eliminar acentos (útil para español)
    private string RemoveAccents(string text)
    {
        StringBuilder sb = new StringBuilder();
        foreach (char c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
