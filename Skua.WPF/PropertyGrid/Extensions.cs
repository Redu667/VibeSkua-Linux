using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Markup.Primitives;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Skua.WPF;

internal static class Extensions
{
    private const string _hexaChars = "0123456789ABCDEF";

    public static string ToHexa(byte[] bytes)
    {
        return bytes == null ? null : ToHexa(bytes, 0, bytes.Length);
    }

    public static string ToHexa(byte[] bytes, int offset, int count)
    {
        if (bytes == null)
            return string.Empty;

        if (offset < 0)
            throw new ArgumentException(null, "offset");

        if (count < 0)
            throw new ArgumentException(null, "count");

        if (offset >= bytes.Length)
            return string.Empty;

        count = Math.Min(count, bytes.Length - offset);

        StringBuilder sb = new(count * 2);
        for (int i = offset; i < (offset + count); i++)
        {
            sb.Append(_hexaChars[bytes[i] / 16]);
            sb.Append(_hexaChars[bytes[i] % 16]);
        }
        return sb.ToString();
    }

    public static object EnumToObject(Type enumType, object value)
    {
        if (enumType == null)
            throw new ArgumentNullException("enumType");

        if (!enumType.IsEnum)
            throw new ArgumentException(null, "enumType");

        if (value == null)
            throw new ArgumentNullException("value");

        Type underlyingType = Enum.GetUnderlyingType(enumType);
        if (underlyingType == typeof(long))
            return Enum.ToObject(enumType, ConversionService.ChangeType<long>(value));

        if (underlyingType == typeof(ulong))
            return Enum.ToObject(enumType, ConversionService.ChangeType<ulong>(value));

        if (underlyingType == typeof(int))
            return Enum.ToObject(enumType, ConversionService.ChangeType<int>(value));

        if (underlyingType == typeof(uint))
            return Enum.ToObject(enumType, ConversionService.ChangeType<uint>(value));

        return underlyingType == typeof(short)
            ? Enum.ToObject(enumType, ConversionService.ChangeType<short>(value))
            : underlyingType == typeof(ushort)
            ? Enum.ToObject(enumType, ConversionService.ChangeType<ushort>(value))
            : underlyingType == typeof(byte)
            ? Enum.ToObject(enumType, ConversionService.ChangeType<byte>(value))
            : underlyingType == typeof(sbyte)
            ? Enum.ToObject(enumType, ConversionService.ChangeType<sbyte>(value))
            : throw new ArgumentException(null, "enumType");
    }

    public static string Format(object obj, string format, IFormatProvider formatProvider)
    {
        if (obj == null)
            return string.Empty;

        if (string.IsNullOrEmpty(format))
            return obj.ToString();

        if (format.StartsWith("*") ||
            format.StartsWith("#"))
        {
            char sep1 = ' ';
            char sep2 = ':';
            if (format.Length > 1)
            {
                sep1 = format[1];
            }
            if (format.Length > 2)
            {
                sep2 = format[2];
            }

            StringBuilder sb = new();
            foreach (PropertyInfo pi in obj.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!pi.CanRead)
                    continue;

                if (pi.GetIndexParameters().Length > 0)
                    continue;

                object value;
                try
                {
                    value = pi.GetValue(obj, null);
                }
                catch
                {
                    continue;
                }
                if (sb.Length > 0)
                {
                    if (sep1 != ' ')
                    {
                        sb.Append(sep1);
                    }
                    sb.Append(' ');
                }

                if (format[0] == '#')
                {
                    sb.Append(DecamelizationService.Decamelize(pi.Name));
                }
                else
                {
                    sb.Append(pi.Name);
                }
                sb.Append(sep2);
                sb.Append(ConversionService.ChangeType(value, string.Format("{0}", value), formatProvider));
            }
            return sb.ToString();
        }

        if (format.StartsWith("Item[", StringComparison.CurrentCultureIgnoreCase))
        {
            string enumExpression;
            int exprPos = format.IndexOf(']', 5);
            enumExpression = exprPos < 0 ? string.Empty : format[5..exprPos].Trim();

            if (obj is IEnumerable enumerable)
            {
                format = format[(6 + enumExpression.Length)..];
                string expression;
                string separator;
                if (format.Length == 0)
                {
                    expression = null;
                    separator = ",";
                }
                else
                {
                    int pos = format.IndexOf(',');
                    if (pos <= 0)
                    {
                        separator = ",";
                        // skip '.'
                        expression = format[1..];
                    }
                    else
                    {
                        separator = format[(pos + 1)..];
                        expression = format[1..pos];
                    }
                }
                return ConcatenateCollection(enumerable, expression, separator, formatProvider);
            }
        }
        else if (format.IndexOf(',') >= 0)
        {
            StringBuilder sb = new();
            foreach (string propName in format.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                PropertyInfo pi = obj.GetType().GetProperty(propName, BindingFlags.Instance | BindingFlags.Public);
                if ((pi == null) || (!pi.CanRead))
                    continue;

                if (pi.GetIndexParameters().Length > 0)
                    continue;

                object value;
                try
                {
                    value = pi.GetValue(obj, null);
                }
                catch
                {
                    continue;
                }
                if (sb.Length > 0)
                {
                    sb.Append(' ');
                }
                sb.Append(pi.Name);
                sb.Append(':');
                sb.AppendFormat(formatProvider, "{0}", value);
            }
            return sb.ToString();
        }

        int pos2 = format.IndexOf(':');
        if (pos2 > 0)
        {
            object inner = DataBindingEvaluator.Eval(obj, format[..pos2], false);
            return inner == null ? string.Empty : string.Format(formatProvider, "{0:" + format[(pos2 + 1)..] + "}", inner);
        }
        return DataBindingEvaluator.Eval(obj, format, formatProvider, null, false);
    }

    public static string ConcatenateCollection(IEnumerable collection, string expression, string separator)
    {
        return ConcatenateCollection(collection, expression, separator, null);
    }

    public static string ConcatenateCollection(IEnumerable collection, string expression, string separator, IFormatProvider formatProvider)
    {
        if (collection == null)
            return null;

        StringBuilder sb = new();
        int i = 0;
        foreach (object o in collection)
        {
            if (i > 0)
            {
                sb.Append(separator);
            }
            else
            {
                i++;
            }

            if (o != null)
            {
                //object e = ConvertUtilities.Evaluate(o, expression, typeof(string), null, formatProvider);
                object e = DataBindingEvaluator.Eval(o, expression, formatProvider, null, false);
                if (e != null)
                {
                    sb.Append(e);
                }
            }
        }
        return sb.ToString();
    }

    public static Type GetElementType(Type collectionType)
    {
        if (collectionType == null)
            throw new ArgumentNullException("collectionType");

        foreach (Type iface in collectionType.GetInterfaces())
        {
            if (!iface.IsGenericType)
                continue;

            if (iface.GetGenericTypeDefinition() == typeof(IDictionary<,>))
                return iface.GetGenericArguments()[1];

            if (iface.GetGenericTypeDefinition() == typeof(IList<>))
                return iface.GetGenericArguments()[0];

            if (iface.GetGenericTypeDefinition() == typeof(ICollection<>))
                return iface.GetGenericArguments()[0];

            if (iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return iface.GetGenericArguments()[0];
        }
        return typeof(object);
    }

    public static int GetEnumMaxPower(Type enumType)
    {
        if (enumType == null)
            throw new ArgumentNullException("enumType");

        if (!enumType.IsEnum)
            throw new ArgumentException(null, "enumType");

        Type utype = Enum.GetUnderlyingType(enumType);
        return GetEnumUnderlyingTypeMaxPower(utype);
    }

    public static int GetEnumUnderlyingTypeMaxPower(Type underlyingType)
    {
        if (underlyingType == null)
            throw new ArgumentNullException("underlyingType");

        return underlyingType == typeof(long) || underlyingType == typeof(ulong)
            ? 64
            : underlyingType == typeof(int) || underlyingType == typeof(uint)
            ? 32
            : underlyingType == typeof(short) || underlyingType == typeof(ushort)
            ? 16
            : underlyingType == typeof(byte) || underlyingType == typeof(sbyte) ? 8 : throw new ArgumentException(null, "underlyingType");
    }

    public static ulong EnumToUInt64(object value)
    {
        if (value == null)
            throw new ArgumentNullException("value");

        TypeCode typeCode = Convert.GetTypeCode(value);
        return typeCode switch
        {
            TypeCode.SByte or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64 => (ulong)Convert.ToInt64(value, CultureInfo.InvariantCulture),
            TypeCode.Byte or TypeCode.UInt16 or TypeCode.UInt32 or TypeCode.UInt64 => Convert.ToUInt64(value, CultureInfo.InvariantCulture),
            //case TypeCode.String:
            _ => ConversionService.ChangeType<ulong>(value),
        };
    }

    public static bool IsFlagsEnum(Type type)
    {
        return type == null ? throw new ArgumentNullException("type") : type.IsEnum && type.IsDefined(typeof(FlagsAttribute), true);
    }

    public static List<T> SplitToList<T>(this string thisString, params char[] separators)
    {
        List<T> list = new();
        if (thisString != null)
        {
            foreach (string s in thisString.Split(separators))
            {
                T item = ConversionService.ChangeType<T>(s);
                list.Add(item);
            }
        }
        return list;
    }

    public static string Nullify(this string thisString)
    {
        return Nullify(thisString, true);
    }

    public static string Nullify(this string thisString, bool trim)
    {
        return string.IsNullOrWhiteSpace(thisString) ? null : trim ? thisString.Trim() : thisString;
    }

    public static bool EqualsIgnoreCase(this string thisString, string text)
    {
        return EqualsIgnoreCase(thisString, text, false);
    }

    public static bool EqualsIgnoreCase(this string thisString, string text, bool trim)
    {
        if (trim)
        {
            thisString = Nullify(thisString, true);
            text = Nullify(text, true);
        }

        return thisString == null
            ? text == null
            : text != null && thisString.Length == text.Length && string.Compare(thisString, text, StringComparison.OrdinalIgnoreCase) == 0;
    }

    public static IEnumerable<DependencyObject> EnumerateVisualChildren(this DependencyObject obj)
    {
        return obj.EnumerateVisualChildren(true);
    }

    public static IEnumerable<DependencyObject> EnumerateVisualChildren(this DependencyObject obj, bool recursive)
    {
        return obj.EnumerateVisualChildren(recursive, true);
    }

    public static IEnumerable<DependencyObject> EnumerateVisualChildren(this DependencyObject obj, bool recursive, bool sameLevelFirst)
    {
        if (obj == null)
            yield break;

        if (sameLevelFirst)
        {
            int count = VisualTreeHelper.GetChildrenCount(obj);
            List<DependencyObject> list = new(count);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(obj, i);
                if (child == null)
                    continue;

                yield return child;
                if (recursive)
                {
                    list.Add(child);
                }
            }

            foreach (DependencyObject child in list)
            {
                foreach (DependencyObject grandChild in child.EnumerateVisualChildren(recursive, true))
                {
                    yield return grandChild;
                }
            }
        }
        else
        {
            int count = VisualTreeHelper.GetChildrenCount(obj);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(obj, i);
                if (child == null)
                    continue;

                yield return child;
                if (recursive)
                {
                    foreach (DependencyObject dp in child.EnumerateVisualChildren(true, false))
                    {
                        yield return dp;
                    }
                }
            }
        }
    }

    public static T FindVisualChild<T>(this DependencyObject obj, Func<T, bool> where) where T : FrameworkElement
    {
        if (where == null)
            throw new ArgumentNullException("where");

        foreach (T item in obj.EnumerateVisualChildren(true, true).OfType<T>())
        {
            if (where(item))
                return item;
        }
        return null;
    }

    public static T FindVisualChild<T>(this DependencyObject obj, string name) where T : FrameworkElement
    {
        foreach (T item in obj.EnumerateVisualChildren(true, true).OfType<T>())
        {
            if (name == null)
                return item;

            if (item.Name == name)
                return item;
        }
        return null;
    }

    public static IEnumerable<DependencyProperty> EnumerateMarkupDependencyProperties(object element)
    {
        if (element != null)
        {
            MarkupObject markupObject = MarkupWriter.GetMarkupObjectFor(element);
            if (markupObject != null)
            {
                foreach (MarkupProperty mp in markupObject.Properties)
                {
                    if (mp.DependencyProperty != null)
                        yield return mp.DependencyProperty;
                }
            }
        }
    }

    public static IEnumerable<DependencyProperty> EnumerateMarkupAttachedProperties(object element)
    {
        if (element != null)
        {
            MarkupObject markupObject = MarkupWriter.GetMarkupObjectFor(element);
            if (markupObject != null)
            {
                foreach (MarkupProperty mp in markupObject.Properties)
                {
                    if (mp.IsAttached)
                        yield return mp.DependencyProperty;
                }
            }
        }
    }

    public static T GetVisualSelfOrParent<T>(this DependencyObject source) where T : DependencyObject
    {
        return source == null
            ? default
            : source is T
            ? (T)source
            : source is not Visual and not Visual3D ? default : VisualTreeHelper.GetParent(source).GetVisualSelfOrParent<T>();
    }

    public static T FindFocusableVisualChild<T>(this DependencyObject obj, string name) where T : FrameworkElement
    {
        foreach (T item in obj.EnumerateVisualChildren(true, true).OfType<T>())
        {
            if (item.Focusable && (item.Name == name || name == null))
                return item;
        }
        return null;
    }

    public static IEnumerable<T> GetChildren<T>(this DependencyObject obj)
    {
        if (obj == null)
            yield break;

        foreach (object item in LogicalTreeHelper.GetChildren(obj))
        {
            if (item == null)
                continue;

            if (item is T)
                yield return (T)item;

            if (item is DependencyObject dep)
            {
                foreach (T child in dep.GetChildren<T>())
                {
                    yield return child;
                }
            }
        }
    }

    public static T GetSelfOrParent<T>(this FrameworkElement source) where T : FrameworkElement
    {
        while (true)
        {
            if (source == null)
                return default;

            if (source is T)
                return (T)source;

            source = source.Parent as FrameworkElement;
        }
    }

    public static string GetAllMessages(this Exception exception)
    {
        return GetAllMessages(exception, Environment.NewLine);
    }

    public static string GetAllMessages(this Exception exception, string separator)
    {
        if (exception == null)
            return null;

        StringBuilder sb = new();
        AppendMessages(sb, exception, separator);
        return sb.ToString().Replace("..", ".");
    }

    private static void AppendMessages(StringBuilder sb, Exception e, string separator)
    {
        if (e == null)
            return;

        // this one is not interesting...
        if (e is not TargetInvocationException)
        {
            if (sb.Length > 0)
            {
                sb.Append(separator);
            }
            sb.Append(e.Message);
        }
        AppendMessages(sb, e.InnerException, separator);
    }

    public static T GetAttribute<T>(this ICustomAttributeProvider provider) where T : Attribute
    {
        if (provider == null)
            return null;

        object[] o = provider.GetCustomAttributes(typeof(T), true);
        return o == null || o.Length == 0 ? null : (T)o[0];
    }

    public static T GetAttribute<T>(this MemberDescriptor descriptor) where T : Attribute
    {
        return descriptor == null ? null : GetAttribute<T>(descriptor.Attributes);
    }

    public static T GetAttribute<T>(this AttributeCollection attributes) where T : Attribute
    {
        if (attributes == null)
            return null;

        foreach (object? att in attributes)
        {
            if (typeof(T).IsAssignableFrom(att.GetType()))
                return (T)att;
        }
        return null;
    }

    public static IEnumerable<T> GetAttributes<T>(this MemberInfo element) where T : Attribute
    {
        return (IEnumerable<T>)Attribute.GetCustomAttributes(element, typeof(T));
    }

    public static bool IsNullable(this Type type)
    {
        return type != null && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>);
    }
}