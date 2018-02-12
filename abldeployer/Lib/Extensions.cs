#region header

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
using System.Linq;
using System.Text.RegularExpressions;

namespace abldeployer.Lib {
    /// <summary>
    ///     This class regroups all the extension methods
    /// </summary>
    public static class Extensions {
        private static Dictionary<Type, List<Tuple<string, long>>> _enumTypeNameValueKeyPairs = new Dictionary<Type, List<Tuple<string, long>>>();

        public static void ForEach<T>(this Type curType, Action<string, long> actionForEachNameValue) {
            if (!curType.IsEnum)
                return;
            if (!_enumTypeNameValueKeyPairs.ContainsKey(curType)) {
                var list = new List<Tuple<string, long>>();
                foreach (var name in Enum.GetNames(curType)) {
                    var val = (T) Enum.Parse(curType, name);
                    list.Add(new Tuple<string, long>(name, Convert.ToInt64(val)));
                }
                _enumTypeNameValueKeyPairs.Add(curType, list);
            }
            foreach (var tuple in _enumTypeNameValueKeyPairs[curType]) actionForEachNameValue(tuple.Item1, tuple.Item2);
        }

        /// <summary>
        ///     Converts a string to an object of the given type
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
        ///     Allows to test if a string matches one of the listOfPattern (wildcards) in the list of patterns,
        ///     Ex : "file.xml".TestAgainstListOfPatterns("*.xls,*.com,*.xml") return true
        /// </summary>
        public static bool TestAgainstListOfPatterns(this string source, string listOfPattern) {
            return listOfPattern.Split(',').Where(s => !string.IsNullOrEmpty(s)).ToList().Exists(s => source.RegexMatch(s.WildCardToRegex()));
        }

        /// <summary>
        ///     Equivalent to Equals but case insensitive
        /// </summary>
        /// <param name="s"></param>
        /// <param name="comp"></param>
        /// <returns></returns>
        public static bool EqualsCi(this string s, string comp) {
            //string.Equals(a, b, StringComparison.CurrentCultureIgnoreCase);
            return s.Equals(comp, StringComparison.CurrentCultureIgnoreCase);
        }

        /// <summary>
        ///     case insensitive contains
        /// </summary>
        /// <param name="source"></param>
        /// <param name="toCheck"></param>
        /// <returns></returns>
        public static bool ContainsFast(this string source, string toCheck) {
            return source.IndexOf(toCheck, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        /// <summary>
        ///     Allows to tranform a matching string using * and ? (wildcards) into a valid regex expression
        ///     it escapes regex special char so it will work as you expect!
        ///     Ex: foo*.xls? will become ^foo.*\.xls.$
        ///     if the listOfPattern doesn't start with a * and doesn't end with a *, it adds both
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
        ///     Allows to find a string with a regular expression, uses the IgnoreCase option by default, returns a match
        ///     collection,
        ///     to be used foreach (Match match in collection) { with match.Groups[1].Value being the first capture [2] etc...
        /// </summary>
        public static MatchCollection RegexFind(this string source, string regexString, RegexOptions options = RegexOptions.IgnoreCase) {
            var regex = new Regex(regexString, options);
            return regex.Matches(source);
        }

        /// <summary>
        ///     Allows to test a string with a regular expression, uses the IgnoreCase option by default
        ///     good website : https://regex101.com/
        /// </summary>
        public static bool RegexMatch(this string source, string regex, RegexOptions options = RegexOptions.IgnoreCase) {
            var reg = new Regex(regex, options);
            return reg.Match(source).Success;
        }

        /// <summary>
        ///     Allows to replace a string with a regular expression, uses the IgnoreCase option by default,
        ///     replacementStr can contains $1, $2...
        /// </summary>
        public static string RegexReplace(this string source, string regexString, string replacementStr, RegexOptions options = RegexOptions.IgnoreCase) {
            var regex = new Regex(regexString, options);
            return regex.Replace(source, replacementStr);
        }

        /// <summary>
        ///     Allows to replace a string with a regular expression, uses the IgnoreCase option by default
        /// </summary>
        public static string RegexReplace(this string source, string regexString, MatchEvaluator matchEvaluator, RegexOptions options = RegexOptions.IgnoreCase) {
            var regex = new Regex(regexString, options);
            return regex.Replace(source, matchEvaluator);
        }

        /// <summary>
        ///     Replaces " by "", replaces new lines by spaces and add extra " at the start and end of the string
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public static string Quoter(this string text) {
            return "\"" + (text ?? "").Replace("\"", "\"\"").Replace("\n", " ").Replace("\r", "") + "\"";
        }

        /// <summary>
        ///     Format a text to use as a single line CHARACTER string encapsulated in double quotes "
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        private static string ProQuoter(this string text) {
            return "\"" + (text ?? "").Replace("\"", "~\"").Replace("\\", "~\\").Replace("/", "~/").Replace("*", "~*").Replace("\n", "~n").Replace("\r", "~r") + "\"";
        }

        /// <summary>
        ///     Uses ProQuoter then make sure to escape every ~ with a double ~~
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public static string PreProcQuoter(this string text) {
            return text.ProQuoter().Replace("~", "~~");
        }

        /// <summary>
        ///     Make sure the directory finished with "\"
        /// </summary>
        public static string CorrectDirPath(this string path) {
            return path.TrimEnd('\\') + @"\";
        }

        /// <summary>
        ///     Same as ToList but returns an empty list on Null instead of an exception
        /// </summary>
        public static List<T> ToNonNullList<T>(this IEnumerable<T> obj) {
            return obj == null ? new List<T>() : obj.ToList();
        }

        /// <summary>
        ///     Returns true if the ftp uri is valid
        /// </summary>
        public static bool IsValidFtpAdress(this string ftpUri) {
            return new Regex(@"^(ftps?:\/\/([^:\/@]*)?(:[^:\/@]*)?(@[^:\/@]*)?(:[^:\/@]*)?)(\/.*)$").Match(ftpUri.Replace("\\", "/")).Success;
        }
    }
}