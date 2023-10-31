
using System;
using System.Linq;


namespace MVP.DiscordBot
{
    using Newtonsoft.Json;
    using System.Collections;
    using System.ComponentModel;
    using System.Text;

    public static class Extensions
    {

        public static string ConcatMessages(this Exception that, string delimiter = ", ")
        {
            var messages = new StringBuilder();
            ConcatMessages(that, ref messages, delimiter);
            return messages.ToString();
        }

        private static void ConcatMessages(Exception ex, ref StringBuilder Messages, string delimiter)
        {
            if (ex == null || Messages == null) return;
            if (ex.InnerException == null)
                ConcatMessages(ex.InnerException, ref Messages, delimiter);

            if (!string.IsNullOrWhiteSpace(ex.Message))
            {
                if (Messages.Length > 0)
                {
                    Messages.Append(delimiter);
                }
                Messages.Append(ex.Message);
            }
        }
        public static string GetDescription<T>(this T that) where T : struct
        {
            AssertIsEnum<T>(false);
            var name = Enum.GetName(typeof(T), that);
            if (name == null) return string.Empty;

            var field = typeof(T).GetField(name);
            if (field != null && Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute)) is DescriptionAttribute attr)
            {
                return attr.Description;
            }
            return string.Empty;
        }
        public static string ToJsonString(this object that, bool indented = false)
        {
            return JsonConvert.SerializeObject(that, indented ? Formatting.Indented : Formatting.None);
        }
        public static T ToEnum<T>(this string that) where T : struct
        {
            if (that == null)
                return default;
            return Enum.GetNames(typeof(T))
                .Select(Enum.Parse<T>)
                .FirstOrDefault(parsed => that.ToLower() == parsed.GetDescription());
        }

        public static void AssertIsEnum<T>(bool withFlags)
        {
            if (!typeof(T).IsEnum)
                throw new ArgumentException($"Type '{typeof(T).FullName}' is not an enum");
            if (withFlags && !Attribute.IsDefined(typeof(T), typeof(FlagsAttribute)))
                throw new ArgumentException($"Type '{typeof(T).FullName}s' does not have the 'Flags' attribute.");
        }

        public static decimal? ToDecimal(this object o)
        {
            return decimal.TryParse(o?.ToString(), out var r) ? (decimal?)r : null;
        }

        public static T CastOrDefault<T>(this object o)
        {
            try
            {
                T result = (T)o;
                return result;
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                return default;
            }
        }

        public static void ActIfNull(this object that, Action a)
        {
            if (that == null) a();
        }
        public static void ActIfEmpty(this ICollection that, Action a)
        {
            if (that.Count == 0) a();
        }
        public static void ActIfEmpty(this string that, Action a)
        {
            if (that.Equals(string.Empty)) a();
        }
        public static void ActIfNotEmpty(this ICollection that, Action a)
        {
            if (that.Count != 0) a();
        }
        public static void ActIfNotNull(this object that, Action a)
        {
            if (that != null) a();
        }
        public static void ThrowIfNull<T>(this object that) where T : Exception, new()
        {
            that.ActIfNull(() => throw (T)Activator.CreateInstance(typeof(T)));
        }
        public static void ThrowIfNull<T>(this object that, string message) where T : Exception, new()
        {
            that.ActIfNull(() => throw (T)Activator.CreateInstance(typeof(T), message));
        }
        public static void ThrowIfEmpty<T>(this ICollection that) where T : Exception, new()
        {
            that.ActIfEmpty(() => throw (T)Activator.CreateInstance(typeof(T)));
        }
        public static void ThrowIfEmpty<T>(this ICollection that, string message) where T : Exception, new()
        {
            that.ActIfEmpty(() => throw (T)Activator.CreateInstance(typeof(T), message));
        }
        public static void ThrowIfEmpty<T>(this string that) where T : Exception, new()
        {
            if (that.Equals(string.Empty)) that.ActIfEmpty(() => throw (T)Activator.CreateInstance(typeof(T)));
        }
        public static void ThrowIfEmpty<T>(this string that, string message) where T : Exception, new()
        {
            if (that.Equals(string.Empty)) that.ActIfEmpty(() => throw (T)Activator.CreateInstance(typeof(T), message));
        }
        public static void ActIfNullOrEmpty(this ICollection that, Action a)
        {
            that.ActIfNull(a);
            that.ActIfEmpty(a);
        }
        public static void ThrowIfNullOrEmpty<T>(this ICollection that, string message) where T : Exception, new()
        {
            that.ThrowIfNull<T>(message);
            that.ThrowIfEmpty<T>(message);
        }
        public static void ThrowIfNullOrEmpty<T>(this ICollection that) where T : Exception, new()
        {
            that.ThrowIfNull<T>();
            that.ThrowIfEmpty<T>();
        }

        public static void ThrowIfNullOrEmpty<T>(this string that, string message) where T : Exception, new()
        {
            that.ThrowIfNull<T>(message);
            that.ThrowIfEmpty<T>(message);
        }
        public static void ThrowIfNullOrEmpty<T>(this string that) where T : Exception, new()
        {
            that.ThrowIfNull<T>();
            that.ThrowIfEmpty<T>();
        }

    }


}
