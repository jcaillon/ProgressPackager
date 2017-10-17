﻿#region header
// ========================================================================
// Copyright (c) 2017 - Julien Caillon (julien.caillon@gmail.com)
// This file (Extensions.cs) is part of csdeployer.
// 
// csdeployer is a free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// csdeployer is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with csdeployer. If not, see <http://www.gnu.org/licenses/>.
// ========================================================================
#endregion
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace csdeployer.Lib {
    /// <summary>
    /// This class regroups all the extension methods
    /// </summary>
    public static class Extensions {
        #region object

        /// <summary>
        /// Use : var name = player.GetAttributeFrom DisplayAttribute>("PlayerDescription").Name;
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="instance"></param>
        /// <param name="propertyName"></param>
        /// <returns></returns>
        public static T GetAttributeFrom<T>(this object instance, string propertyName) where T : Attribute {
            var attrType = typeof(T);
            var property = instance.GetType().GetProperty(propertyName);
            return (T) property.GetCustomAttributes(attrType, false).First();
        }

        /// <summary>
        /// Returns true of the given object has the given method
        /// </summary>
        /// <param name="objectToCheck"></param>
        /// <param name="methodName"></param>
        /// <returns></returns>
        public static bool HasMethod(this object objectToCheck, string methodName) {
            try {
                var type = objectToCheck.GetType();
                return type.GetMethod(methodName) != null;
            } catch (AmbiguousMatchException) {
                // ambiguous means there is more than one result,
                // which means: a method with that name does exist
                return true;
            }
        }

        /// <summary>
        /// Invoke the given method with the given parameters on the given object and returns its value
        /// Returns null if it failes
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="methodName"></param>
        /// <param name="parameters"></param>
        /// <returns></returns>
        public static object InvokeMethod(this object obj, string methodName, object[] parameters) {
            try {
                //Get the method information using the method info class
                MethodInfo mi = obj.GetType().GetMethod(methodName);
                return mi != null ? mi.Invoke(obj, parameters) : null;
            } catch (Exception) {
                return null;
            }
        }

        /// <summary>
        /// Executes an action on the thread of the given object
        /// </summary>
        public static void SafeInvoke<T>(this T isi, Action<T> call) where T : ISynchronizeInvoke {
            if (isi.InvokeRequired) isi.BeginInvoke(call, new object[] {isi});
            else
                call(isi);
        }

        #endregion

        #region Enumerable

        /// <summary>
        /// Same as ToList but returns an empty list on Null instead of an exception
        /// </summary>
        public static List<T> ToNonNullList<T>(this IEnumerable<T> obj) {
            return obj == null ? new List<T>() : obj.ToList();
        }

        /// <summary>
        /// Find the index of the first element satisfaying the predicate
        /// </summary>
        public static int FindIndex<T>(this IEnumerable<T> items, Func<T, bool> predicate) {
            if (predicate == null) throw new ArgumentNullException("predicate");
            int retVal = 0;
            foreach (var item in items) {
                if (predicate(item)) return retVal;
                retVal++;
            }
            return -1;
        }

        /// <summary>
        /// Find the index of the first element equals to itemToFind
        /// </summary>
        public static int IndexOf<T>(this IEnumerable<T> items, T itemToFind) {
            int retVal = 0;
            foreach (var item in items) {
                if (item.Equals(itemToFind)) return retVal;
                retVal++;
            }
            return -1;
        }

        #endregion

        #region int

        /// <summary>
        /// Returns true if the bit at the given position is set to true
        /// </summary>
        public static bool IsBitSet(this int b, int pos) {
            return (b & (1 << pos)) != 0;
        }

        /// <summary>
        /// Returns true if the bit at the given position is set to true
        /// </summary>
        public static bool IsBitSet(this uint b, int pos) {
            return (b & (1 << pos)) != 0;
        }

        /// <summary>
        /// Returns true if the bit at the given position is set to true
        /// </summary>
        public static bool IsBitSet(this long b, int pos) {
            return (b & (1 << pos)) != 0;
        }

        #endregion

        #region Colors

        /// <summary>
        /// returns true if the color can be considered as dark
        /// </summary>
        public static bool IsColorDark(this Color color) {
            return color.GetBrightness() < 0.5;
        }

        #endregion

        #region Enum and attributes extensions

        private static Dictionary<Type, List<Tuple<string, long>>> _enumTypeNameValueKeyPairs = new Dictionary<Type, List<Tuple<string, long>>>();

        public static void ForEach<T>(this Type curType, Action<string, long> actionForEachNameValue) {
            if (!curType.IsEnum)
                return;
            if (!_enumTypeNameValueKeyPairs.ContainsKey(curType)) {
                var list = new List<Tuple<string, long>>();
                foreach (var name in Enum.GetNames(curType)) {
                    T val = (T) Enum.Parse(curType, name);
                    list.Add(new Tuple<string, long>(name, Convert.ToInt64(val)));
                }
                _enumTypeNameValueKeyPairs.Add(curType, list);
            }
            foreach (var tuple in _enumTypeNameValueKeyPairs[curType]) {
                actionForEachNameValue(tuple.Item1, tuple.Item2);
            }
        }

        /// <summary>
        /// Returns the attribute array for the given Type T and the given value,
        /// not to self : dont use that on critical path -> reflection is costly
        /// </summary>
        public static T[] GetAttributes<T>(this Enum value) where T : Attribute {
            Type type = value.GetType();
            string name = Enum.GetName(type, value);
            if (name != null) {
                FieldInfo field = type.GetField(name);
                if (field != null) {
                    var attributeArray = (T[]) Attribute.GetCustomAttributes(field, typeof(T), true);
                    return attributeArray;
                }
            }
            return null;
        }

        /// <summary>
        /// Returns the attribute for the given Type T and the given value,
        /// not to self : dont use that on critical path -> reflection is costly
        /// </summary>
        public static T GetAttribute<T>(this Enum value) where T : Attribute {
            Type type = value.GetType();
            string name = Enum.GetName(type, value);
            if (name != null) {
                FieldInfo field = type.GetField(name);
                if (field != null) {
                    var attribute = Attribute.GetCustomAttribute(field, typeof(T), true) as T;
                    return attribute;
                }
            }
            return null;
        }

        /// <summary>
        /// Allows to describe a field of an enum like this :
        /// [EnumAttribute(Value = "DATA-SOURCE")]
        /// and use the value "Value" with :
        /// currentOperation.GetAttribute!EnumAttribute>().Value 
        /// </summary>
        [AttributeUsage(AttributeTargets.Field)]
        public class EnumAttribute : Attribute { }

        /// <summary>
        /// Decorate enum values with [Description("Description for Foo")] and get their description with x.Foo.GetDescription()
        /// </summary>
        public static string GetDescription(this Enum value) {
            var attr = value.GetAttribute<DescriptionAttribute>();
            return attr != null ? attr.Description : null;
        }

        /// <summary>
        /// MyEnum tester = MyEnum.FlagA | MyEnum.FlagB;
        /// if(tester.IsSet(MyEnum.FlagA))
        /// </summary>
        public static bool IsFlagSet(this Enum input, Enum matchTo) {
            return (Convert.ToUInt32(input) & Convert.ToUInt32(matchTo)) != 0;
        }

        //flags |= flag;// SetFlag
        //flags &= ~flag; // ClearFlag 

        #endregion

        #region string extensions

        /// <summary>
        /// Converts a string to an object of the given type
        /// </summary>
        public static object ConvertFromStr(this string value, Type destType) {
            try {
                if (destType == typeof(string))
                    return value;
                return TypeDescriptor.GetConverter(destType).ConvertFromInvariantString(value);
            } catch (Exception) {
                return destType.IsValueType ? Activator.CreateInstance(destType) : null;
            }
        }

        /// <summary>
        /// Returns true if the http uri is valid
        /// </summary>
        public static bool IsValidHtmlAdress(this string http) {
            return new Regex(@"^(ht|f)tp(s?)\:\/\/[0-9a-zA-Z]([-.\w]*[0-9a-zA-Z])*(:(0-9)*)*(\/?)([a-zA-Z0-9\-\.\?\,\'\/\\\+&amp;%\$#_]*)?$").Match(http).Success;
        }

        /// <summary>
        /// Returns true if the ftp uri is valid
        /// </summary>
        public static bool IsValidFtpAdress(this string ftpUri) {
            return new Regex(@"^(ftps?:\/\/([^:\/@]*)?(:[^:\/@]*)?(@[^:\/@]*)?(:[^:\/@]*)?)(\/.*)$").Match(ftpUri.Replace("\\", "/")).Success;
        }

        /// <summary>
        /// Allows to test if a string matches one of the listOfPattern (wildcards) in the list of patterns,
        /// Ex : "file.xml".TestAgainstListOfPatterns("*.xls,*.com,*.xml") return true
        /// </summary>
        public static bool TestAgainstListOfPatterns(this string source, string listOfPattern) {
            return listOfPattern.Split(',').Where(s => !string.IsNullOrEmpty(s)).ToList().Exists(s => source.RegexMatch(s.WildCardToRegex()));
        }

        /// <summary>
        /// Allows to test a string with a regular expression, uses the IgnoreCase option by default
        /// good website : https://regex101.com/
        /// </summary>
        public static bool RegexMatch(this string source, string regex, RegexOptions options = RegexOptions.IgnoreCase) {
            var reg = new Regex(regex, options);
            return reg.Match(source).Success;
        }

        /// <summary>
        /// Allows to replace a string with a regular expression, uses the IgnoreCase option by default,
        /// replacementStr can contains $1, $2...
        /// </summary>
        public static string RegexReplace(this string source, string regexString, string replacementStr, RegexOptions options = RegexOptions.IgnoreCase) {
            var regex = new Regex(regexString, options);
            return regex.Replace(source, replacementStr);
        }

        /// <summary>
        /// Allows to replace a string with a regular expression, uses the IgnoreCase option by default
        /// </summary>
        public static string RegexReplace(this string source, string regexString, MatchEvaluator matchEvaluator, RegexOptions options = RegexOptions.IgnoreCase) {
            var regex = new Regex(regexString, options);
            return regex.Replace(source, matchEvaluator);
        }

        /// <summary>
        /// Allows to find a string with a regular expression, uses the IgnoreCase option by default, returns a match collection,
        /// to be used foreach (Match match in collection) { with match.Groups[1].Value being the first capture [2] etc...
        /// </summary>
        public static MatchCollection RegexFind(this string source, string regexString, RegexOptions options = RegexOptions.IgnoreCase) {
            var regex = new Regex(regexString, options);
            return regex.Matches(source);
        }

        /// <summary>
        /// Allows to tranform a matching string using * and ? (wildcards) into a valid regex expression
        /// it escapes regex special char so it will work as you expect!
        /// Ex: foo*.xls? will become ^foo.*\.xls.$
        /// if the listOfPattern doesn't start with a * and doesn't end with a *, it adds both
        /// </summary>
        /// <param name="pattern"></param>
        /// <returns></returns>
        public static string WildCardToRegex(this string pattern) {
            if (string.IsNullOrEmpty(pattern))
                return ".*";
            var startStar = pattern[0].Equals('*');
            var endStar = pattern[pattern.Length - 1].Equals('*');
            return (!startStar ? (endStar ? "^" : "") : "") + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + (!endStar ? (startStar ? "$" : "") : "");
        }

        /// <summary>
        /// Returns the html link representation from a url
        /// </summary>
        /// <returns></returns>
        public static string ToHtmlLink(this string url, string urlName = null, bool accentuate = false) {
            try {
                if (File.Exists(url) || Directory.Exists(url)) {
                    var splitName = (urlName ?? url).Split('\\');
                    if (urlName == null || splitName.Length > 0) {
                        var splitUrl = url.Split('\\');
                        var output = new StringBuilder();
                        var path = new StringBuilder();
                        var j = 0;
                        for (int i = 0; i < splitUrl.Length; i++) {
                            path.Append(splitUrl[i]);
                            if (splitUrl[i].EqualsCi(splitName[j])) {
                                output.Append(string.Format("<a {3}href='{0}'>{1}</a>{2}", path, splitUrl[i], i < splitUrl.Length - 1 ? "<span class='linkSeparator'>\\</span>" : "", i == splitUrl.Length - 1 && accentuate ? "class='SubTextColor' " : ""));
                                j++;
                            }
                            path.Append("\\");
                        }
                        for (int i = splitUrl.Length; i < splitName.Length; i++) {
                            output.Append("<span class='linkSeparator'>\\</span>");
                            output.Append(splitName[i]);
                        }
                        if (output.Length > 0)
                            return output.ToString();
                    }
                }
            } catch (Exception) {
                // ignored invalid char path
            }
            return string.Format("<a {2}href='{0}'>{1}</a>", url, urlName ?? url, accentuate ? "class='SubTextColor' " : "");
        }

        /// <summary>
        /// Replaces every forbidden char (forbidden for a filename) in the text
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public static string ToValidFileName(this string text) {
            return Path.GetInvalidFileNameChars().Aggregate(text, (current, c) => current.Replace(c, '-'));
        }

        /// <summary>
        /// Replaces " by "", replaces new lines by spaces and add extra " at the start and end of the string
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public static string Quoter(this string text) {
            return "\"" + (text ?? "").Replace("\"", "\"\"").Replace("\n", " ").Replace("\r", "") + "\"";
        }

        /// <summary>
        /// Format a text to use as a single line CHARACTER string encapsulated in double quotes "
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        private static string ProQuoter(this string text) {
            return "\"" + (text ?? "").Replace("\"", "~\"").Replace("\\", "~\\").Replace("/", "~/").Replace("*", "~*").Replace("\n", "~n").Replace("\r", "~r") + "\"";
        }

        /// <summary>
        /// Uses ProQuoter then make sure to escape every ~ with a double ~~
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public static string PreProcQuoter(this string text) {
            return text.ProQuoter().Replace("~", "~~");
        }

        /// <summary>
        /// Breaks new lines every lineLength char, taking into account words to not
        /// split them
        /// </summary>
        /// <param name="text"></param>
        /// <param name="lineLength"></param>
        /// <param name="eolString"></param>
        /// <returns></returns>
        public static string BreakText(this string text, int lineLength, string eolString = "\n") {
            var charCount = 0;
            var lines = text.Split(new[] {" "}, StringSplitOptions.RemoveEmptyEntries)
                .GroupBy(w => (charCount += w.Length + 1) / lineLength)
                .Select(g => String.Join(" ", g));
            return String.Join(eolString, lines.ToArray());
        }

        /// <summary>
        /// Compares two version string "1.0.0.0".IsHigherVersionThan("0.9") returns true
        /// Must be STRICTLY superior
        /// </summary>
        public static bool IsHigherVersionThan(this string localVersion, string distantVersion) {
            return CompareVersions(localVersion, distantVersion, false);
        }

        /// <summary>
        /// Compares two version string "1.0.0.0".IsHigherVersionThan("0.9") returns true
        /// </summary>
        public static bool IsHigherOrEqualVersionThan(this string localVersion, string distantVersion) {
            return CompareVersions(localVersion, distantVersion, true);
        }

        /// <summary>
        /// Make sure the directory finished with "\"
        /// </summary>
        public static string CorrectDirPath(this string path) {
            return path.TrimEnd('\\') + @"\";
        }

        /// <summary>
        /// Returns true if local >(=) distant
        /// </summary>
        /// <returns></returns>
        private static bool CompareVersions(this string localVersion, string distantVersion, bool trueIfEqual) {
            try {
                var splitLocal = localVersion.TrimStart('v').Split('.').Select(s => int.Parse(s.Trim())).ToList();
                var splitDistant = distantVersion.TrimStart('v').Split('.').Select(s => int.Parse(s.Trim())).ToList();
                var i = 0;
                while (i <= (splitLocal.Count - 1) && i <= (splitDistant.Count - 1)) {
                    if (splitLocal[i] > splitDistant[i])
                        return true;
                    if (splitLocal[i] < splitDistant[i])
                        return false;
                    i++;
                }
                if (splitLocal.Sum() == splitDistant.Sum() && trueIfEqual)
                    return true;
                if (splitLocal.Sum() > splitDistant.Sum())
                    return true;
            } catch (Exception) {
                // would happen if the input strings are incorrect
            }
            return false;
        }

        /// <summary>
        /// Check if word contains at least one letter
        /// </summary>
        /// <param name="word"></param>
        /// <returns></returns>
        public static bool ContainsAtLeastOneLetter(this string word) {
            if (string.IsNullOrEmpty(word))
                return false;
            var max = word.Length - 1;
            int count = 0;
            while (count <= max) {
                if (Char.IsLetter(word[count]))
                    return true;
                count++;
            }
            return false;
        }

        /// <summary>
        /// autocase the keyword according to the mode given
        /// </summary>
        public static string ConvertCase(this string keyword, int mode, string naturalCase = null) {
            switch (mode) {
                case 1:
                    return keyword.ToUpper();
                case 2:
                    return keyword.ToLower();
                case 3:
                    return keyword.ToTitleCase();
                default:
                    return naturalCase ?? keyword;
            }
        }

        /// <summary>
        /// Count the nb of occurrences...
        /// </summary>
        /// <param name="haystack"></param>
        /// <param name="needle"></param>
        /// <returns></returns>
        public static int CountOccurences(this string haystack, string needle) {
            return (haystack.Length - haystack.Replace(needle, "").Length) / needle.Length;
        }

        /// <summary>
        /// Equivalent to Equals but case insensitive
        /// </summary>
        /// <param name="s"></param>
        /// <param name="comp"></param>
        /// <returns></returns>
        public static bool EqualsCi(this string s, string comp) {
            //string.Equals(a, b, StringComparison.CurrentCultureIgnoreCase);
            return s.Equals(comp, StringComparison.CurrentCultureIgnoreCase);
        }

        /// <summary>
        /// convert the word to Title Case
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        public static string ToTitleCase(this string s) {
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLower());
        }

        /// <summary>
        /// case insensitive contains
        /// </summary>
        /// <param name="source"></param>
        /// <param name="toCheck"></param>
        /// <returns></returns>
        public static bool ContainsFast(this string source, string toCheck) {
            return source.IndexOf(toCheck, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        /// <summary>
        /// Returns the string given only with acceptable characters for a variable name
        /// </summary>
        public static string MakeValidVariableName(this string source) {
            var outStr = "";
            foreach (char c in source) {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                    outStr += c;
                else if (c == ' ')
                    outStr += '_';
            }
            return outStr;
        }

        /// <summary>
        /// Does the char array contains a given char
        /// </summary>
        /// <param name="array"></param>
        /// <param name="toFind"></param>
        /// <returns></returns>
        public static bool Contains(this char[] array, char toFind) {
            foreach (var chr in array) {
                if (chr == toFind)
                    return true;
            }
            return false;
        }

        #endregion

        #region string builder

        /// <summary>
        /// Append a text X times
        /// </summary>
        public static StringBuilder Append(this StringBuilder builder, string text, int count) {
            for (int i = 0; i < count; i++)
                builder.Append(text);
            return builder;
        }

        public static bool EndsWith(this StringBuilder builder, string pattern) {
            if (builder.Length >= pattern.Length) {
                for (int i = 0; i < pattern.Length; i++)
                    if (pattern[i] != builder[builder.Length - pattern.Length + i])
                        return false;
                return true;
            }
            return false;
        }

        public static StringBuilder TrimEnd(this StringBuilder builder) {
            if (builder.Length > 0) {
                int i;
                for (i = builder.Length - 1; i >= 0; i--)
                    if (!Char.IsWhiteSpace(builder[i]))
                        break;

                builder.Length = i + 1;
            }
            return builder;
        }

        #endregion

        #region Pointers

        public static bool IsTrue(this IntPtr ptr) {
            return ptr != IntPtr.Zero;
        }

        public static IntPtr ToPointer(this bool myBool) {
            return myBool ? new IntPtr(1) : IntPtr.Zero;
        }

        #endregion
    }
}