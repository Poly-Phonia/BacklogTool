using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BackLogTool
{
    /// <summary>
    /// Backlog記法を変換するクラス
    /// </summary>
    public class Notation
    {
        /// <summary>
        /// 変換後の文字列の改行コードを\r\nにするか
        /// </summary>
        public bool UseCrLfInResult { get; set; }

        /// <summary>
        /// ヘッダーがないテーブルがあった場合、１行目をヘッダーにする。falseの場合は空のヘッダー行を作成する。
        /// </summary>
        public bool ForceMakeFirstLineHeader { get; set; }

        public Notation()
        {
            UseCrLfInResult = false;
            ForceMakeFirstLineHeader = false;
        }

        /// <summary>
        /// Backlog記法の文字列をMarkdown記法に変換する
        /// </summary>
        /// <param name="backlogString"></param>
        /// <returns></returns>
        public string ToMarkdown(string backlogString)
        {
            setLayoutPatterns();

            string result = backlogString;
            List<string> codes = new List<string>();
            List<string> quotes = new List<string>();
            List<string> paragraphs = new List<string>();

            //Windows用改行コード(\r\n)を\nのみに変換
            result = result.Replace("\r\n", "\n");

            // 正規表現を助けるための改行を追加
            result = "\n" + result + "\n\n";

            // 目次
            result =
                new RegexMatchReplacer()
                {
                    RegexOptions = RegexOptions.Multiline,
                    Pattern = @"^#contents$",
                    Formatter = match =>
                    {
                        var ret = "[toc]\n";
                        return ret;
                    }
                }.Replace(result);

            // コード範囲指定を隠す
            result =
                new RegexMatchReplacer()
                {
                    Pattern = "\n{code}(?<p1>[\\s|\\S]*?){\\/code}\n",
                    Formatter = match =>
                    {
                        var p1 = match.Groups["p1"].Value;
                        codes.Add(p1);
                        var ret = $"\n{{{{CODE_REPACE_BACKLOG_TO_MARKDOWN-{codes.Count - 1}}}}}\n";
                        return ret;
                    }
                }.Replace(result);

            // 引用範囲指定を隠す
            result =
                new RegexMatchReplacer()
                {
                    Pattern = "\n{quote}(?<p1>[\\s|\\S]*?){\\/quote}\n",
                    Formatter = match =>
                    {
                        var p1 = match.Groups["p1"].Value;
                        quotes.Add(p1);
                        var ret = $"\n{{{{QUOTE_REPACE_BACKLOG_TO_MARKDOWN-{quotes.Count - 1}}}}}\n";
                        return ret;
                    }
                }.Replace(result);

            // 通常テキスト（パラグラフ）を隠す
            result =
                new RegexMatchReplacer()
                {
                    RegexOptions = RegexOptions.Multiline,
                    Pattern = "^.*$",
                    Formatter = match =>
                    {
                        string isP = "^(?![*\\|\\-\\+\\s>)`])(.*)$";
                        var p1 = match.Value;

                        if(!string.IsNullOrEmpty(p1) &&
                           Regex.IsMatch(p1, isP) &&
                           !p1.StartsWith("{{CODE_REPACE_BACKLOG_TO_MARKDOWN") &&
                           !p1.StartsWith("{{QUOTE_REPACE_BACKLOG_TO_MARKDOWN"))
                        {
                            paragraphs.Add(p1);
                            return $"{{{{PARAGRAPHS_REPACE_BACKLOG_TO_MARKDOWN-{paragraphs.Count - 1}}}}}";
                        }
                        else
                        {
                            return p1;
                        }
                    }
                }.Replace(result);

            // パラグラフの塊は最後に空行を開けさせる
            result =
                new RegexMatchReplacer()
                {
                    Pattern = "\n{{PARAGRAPHS_REPACE_BACKLOG_TO_MARKDOWN-.*?}}\n(?!{{)",
                    Formatter = match =>
                    {
                        var ret = $"{match.Value}\n";
                        return ret;
                    }
                }.Replace(result);

            // 範囲指定型以外のBacklog記法を置き換える
            foreach(var r in layoutPatterns)
            {
                result = r.Replace(result);
            }

            // 範囲指定系を埋めもどす前に無駄な改行を削除する
            while(Regex.IsMatch(result, "\n\n\n"))
            {
                result = Regex.Replace(result, "\n\n\n", "\n\n");
            }

            // コード範囲指定を戻す
            result =
                new RegexMatchReplacer()
                {
                    Pattern = "{{CODE_REPACE_BACKLOG_TO_MARKDOWN-(?<p1>.*?)}}",
                    Formatter = match =>
                    {
                        var p1 = match.Groups["p1"].Value;
                        var index = int.Parse(p1);
                        var content = codes[index].Trim();
                        var ret = !string.IsNullOrEmpty(content) ?
                                  $"\n```\n" + content + "\n```\n" :
                                  "\n```\n```\n";
                        return ret;
                    }
                }.Replace(result);

            // 引用範囲指定を戻す
            result =
                new RegexMatchReplacer()
                {
                    Pattern = "{{QUOTE_REPACE_BACKLOG_TO_MARKDOWN-(?<p1>.*?)}}",
                    Formatter = match =>
                    {
                        var p1 = match.Groups["p1"].Value;
                        var index = int.Parse(p1);
                        var content = quotes[index].Trim().Replace("\n", "\n> ");
                        var ret = $"\n> " + content + "\n";
                        return ret;
                    }
                }.Replace(result);

            // パラグラフを戻す
            result =
                new RegexMatchReplacer()
                {
                    Pattern = "{{PARAGRAPHS_REPACE_BACKLOG_TO_MARKDOWN-(?<p1>.*?)}}",
                    Formatter = match =>
                    {
                        var p1 = match.Groups["p1"].Value;
                        var index = int.Parse(p1);
                        var content = textLevelSemanticsCheck(paragraphs[index].Trim());
                        var ret = content;
                        return ret;
                    }
                }.Replace(result);


            return convertLf(result.Trim());
        }

        #region パターン

        /// <summary>
        /// <see cref="textLevelSemanticsCheck"/>で使用する<see cref="IReplacer"/>のリスト
        /// </summary>
        static List<IReplacer> textLevelSemanticsPattern = new List<IReplacer>()
        {
            // 要素を<tagName>表記しているパターンをエスケープ
            new RegexReplacer("<(?<p1>.*?)>", "&lt;${p1}&gt;"),

            // 抜け漏れしている<をエスケープ。>は他の記法で利用されているためエスケープしない
            new RegexReplacer("<", "&lt;"),

            // strong
            new RegexMatchReplacer()
            {
                Pattern = @"''(?<p1>.*?)''",
                Formatter = match =>
                {
                    var p1 = match.Groups["p1"].Value;
                    var result = $" **{p1.Trim()}** ";
                    return result;
                }
            },

            // em
            new RegexMatchReplacer()
            {
                Pattern = @"'(?<p1>.*?)'",
                Formatter = match =>
                {
                    var p1 = match.Groups["p1"].Value;
                    var result = $" *{p1.Trim()}* ";
                    return result;
                }
            },

            // strike
            new RegexMatchReplacer()
            {
                Pattern = @"%%(?<p1>.*?)%%",
                Formatter = match =>
                {
                    var p1 = match.Groups["p1"].Value;
                    var result = $" ~~{p1.Trim()}~~ ";
                    return result;
                }
            },

            // a
            new RegexMatchReplacer()
            {
                Pattern = @"\[\[(?<p1>.*?)[:>](?<p2>.*?)\]\]",
                Formatter = match =>
                {
                    var p1 = match.Groups["p1"].Value;
                    var p2 = match.Groups["p2"].Value;
                    var result = $"[{p1.Trim()}]({p2})";
                    return result;
                }
            },

            // 色指定（Markdown in Backlogでは無視される）
            new RegexMatchReplacer()
            {
                Pattern = @"&color\((?<p1>.*?)\)(?<p2>\s+)?{(?<p3>.*?)}",
                Formatter = match =>
                {
                    var p1 = match.Groups["p1"].Value;
                    var p3 = match.Groups["p3"].Value;
                    var result = $"<span style=\"color: {p1};\">{p3}</span>";
                    return result;
                },
                RegexOptions = RegexOptions.IgnoreCase
            },

            // その他埋め込み
            new RegexMatchReplacer()
            {
                Pattern = @"#image\((?<p1>.*?)\)",
                Formatter = match=>
                {
                    var p1 = match.Groups["p1"].Value;
                    var result = $"![{p1}]";
                    return result;
                }
            },
            new RegexMatchReplacer()
            {
                Pattern = @"#thumbnail\((?<p1>.*?)\)",
                Formatter = match=>
                {
                    var p1 = match.Groups["p1"].Value;
                    var result = $"![{p1}]";
                    return result;
                }
            },
            new RegexMatchReplacer()
            {
                Pattern = @"#attach\((?<p1>.*?):(?<p2>.*?)\)",
                Formatter = match =>
                {
                    var p1 = match.Groups["p1"].Value;
                    var p2 = match.Groups["p2"].Value;
                    var result = $"[{p1}][{p2}]";
                    return result;
                }
            },

            // 余計な半角スペースの連続を削除
            new RegexReplacer(" {2}", " ")
        };


        /// <summary>
        /// 範囲指定以外、かつ１行修飾でないBacklogを変換する<see cref="IReplacer"/>のリスト
        /// </summary>
        private List<IReplacer> layoutPatterns;

        /// <summary>
        /// <see cref="layoutPatterns"/>に内容をセット
        /// </summary>
        void setLayoutPatterns()
        {
            layoutPatterns = new List<IReplacer>()
            {
                // 改行コード
                new RegexMatchReplacer()
                {
                    Pattern = "\n\r",
                    Formatter = match =>
                    {
                        var result = $"\n";
                        return result;
                    }
                },
                new RegexMatchReplacer()
                {
                    Pattern = "\r",
                    Formatter = match =>
                    {
                        var result = $"\n";
                        return result;
                    }
                },

                // 見出し
                new RegexMatchReplacer()
                {
                    RegexOptions = RegexOptions.Multiline,
                    Pattern = "^(?<p1>\\*+)(?<p2>.*)$",
                    Formatter = match =>
                    {
                        var p1 = match.Groups["p1"].Value;
                        var p2 = match.Groups["p2"].Value;

                        var formattedP1 = Regex.Replace(p1, "\\*", "#");

                        //var result = input.Replace(match.Value, $"\n{formattedP1} {textLevelSemanticsCheck(p2.Trim())}\n");
                        var result = $"\n{formattedP1} {textLevelSemanticsCheck(p2.Trim())}\n";
                        return result;
                    }
                },

                // theadなしテーブル
                new RegexMatchReplacer()
                {
                    Pattern = "\n\\|(?<p1>[\\s|\\S]*?)\\|\n(?!\\|)",
                    Formatter = match =>
                    {
                        var p1 = match.Groups["p1"].Value;
                        var result = $"\n|{p1}|\n\n";
                        return result;
                    }
                },
                new RegexMatchReplacer()
                {
                    Pattern = "\n\\|(?<p1>[\\s|\\S]*?)\\|\n(?!\\|)",
                    Formatter = match =>
                    {
                        var p1 = match.Groups["p1"].Value;

                        if (!p1.Contains("|h\n"))
                        {
                            int i = 0;
                            int max = p1.Split('\n')[0].Split('|').Length;
                            string row = "";

                            for(i = 1;i < max; i++) //maxには最終の空白も含めたlengthが入るので、１つ減らすためにi = 1から開始している
                            {
                                row += "|:--";
                            }

                            row += "|";
                            return $"\n{row}\n|{p1}|\n";
                        }

                        return $"\n|{p1}|\n";
                    }
                },

                // 行見出しテーブル
                new RegexMatchReplacer()
                {
                    RegexOptions = RegexOptions.Multiline,
                    Pattern = "^\\|(?<p1>.*)\\|(?<p2>\\s?)$",
                    Formatter = match =>
                    {
                        var p1 = match.Groups["p1"].Value;
                        var p2 = match.Groups["p2"].Value;

                        p1 = Regex.Replace(p1, "\\|~", "|");
                        p1 = Regex.Replace(p1, "^~", "");
                        p1 = Regex.Replace(p1, "\\|\\|", "| |");
                        p1 = Regex.Replace(p1, "^\\|", " |");
                        p1 = Regex.Replace(p1, "^\\|", " |");   // 先頭が空
                        p1 = Regex.Replace(p1, "\\|$", "| ");   // 最後が空

                        var result = $"|{p1}|{p2}";
                        return result;
                    }
                },
                // 列見出しテーブル
                new RegexMatchReplacer()
                {
                    RegexOptions = RegexOptions.Multiline,
                    Pattern = "^\\|(?<p1>.*)\\|h\\s?$",
                    Formatter = match =>
                    {
                        var p1 = match.Groups["p1"].Value;

                        string row = "";
                        var cell = p1.Split('|');
                        int i = 0;
                        int max = cell.Length;

                        for(i = 0;i < max; i++)
                        {
                            row += "|:--";
                        }

                        row += '|';

                        p1 = Regex.Replace(p1, "\\|~", "|");
                        p1 = Regex.Replace(p1, "^~", "");
                        p1 = Regex.Replace(p1, "\\|\\|", "| |");
                        p1 = Regex.Replace(p1, "^\\|", " |");   // 先頭が空
                        p1 = Regex.Replace(p1, "\\|$", "| ");   // 最後が空

                        var result = $"\n|{p1}|\n{row}";
                        return result;
                    }
                },

                // テーブルに改行
                new RegexMatchReplacer()
                {
                    Pattern = "\n\\|(?<p1>[\\s|\\S]*?)\\|\n(?<p2>[^|])",
                    Formatter = match =>
                    {
                        var p1 = match.Groups["p1"].Value;
                        var p2 = match.Groups["p2"].Value;
                        var result = $"\n|{textLevelSemanticsCheck(p1)}|\n\n{p2}";
                        return result;
                    }
                },
                new RegexMatchReplacer()
                {
                    Pattern = "\n\\|(?<p1>[\\s|\\S]*?)\\|\n(?!\\|)",
                    Formatter = match =>
                    {
                        var p1 = match.Groups["p1"].Value;
                        var result = $"\n\n|{p1}|\n\n";
                        return result;
                    }
                },
                new RegexMatchReplacer()    //追加 : ヘッダーがないテーブルの処理
                {
                    RegexOptions = RegexOptions.Multiline,
                    Pattern = "^\n(?<p1>\\|:.*)\n(?<p2>.*$)", //空改行→アラインメント行→1行目
                    Formatter = match =>
                    {
                        var p1 = match.Groups["p1"].Value;  //アラインメント行
                        var p2 = match.Groups["p2"].Value;  //１行目

                        if (ForceMakeFirstLineHeader)
                        {
                            //アラインメント行と１行目を入れ替える
                            return $"\n{p2}\n{p1}";
                        }
                        else
                        {
                            //空のヘッダー行をアラインメント行の上に挿入
                            string blankHeader = Regex.Replace(p1, ":-*", string.Empty);
                            return $"\n{blankHeader}\n{p1}\n{p2}";
                        }
                    }
                },


                // 順序リスト
                new RegexMatchReplacer()
                {
                    Pattern = "\n\\+(?<p1>[\\s|\\S]*?)\n\n",
                    Formatter = match =>
                    {
                        var p1 = match.Groups["p1"].Value;
                        var result = $"\n+{p1}\n\n\n";
                        return result;
                    }
                },
                new RegexMatchReplacer()
                {
                    Pattern = "\n\\+(?<p1>[\\s|\\S]*?)\n\n",
                    Formatter = match =>
                    {
                        var p1 = match.Groups["p1"].Value;

                        p1 = "\n+" + p1.Trim();
                        // スペースの整形
                        var replacer = new RegexMatchReplacer()
                        {
                            RegexOptions = RegexOptions.Multiline,
                            Pattern = "^(?<p2>\\++)(?<p3>.*)$",
                            Formatter = m =>
                            {
                                var p2 = m.Groups["p2"].Value;
                                var p3 = m.Groups["p3"].Value;

                                return $"{p2} {p3.Trim()}";
                            }
                        };
                        p1 = replacer.Replace(p1);
                        p1 = p1.Trim();

                        Func<string, string> lineFormat = str =>
                        {
                            var ret = string.Empty;

                            Dictionary<int, int> symbolCount = new Dictionary<int, int>();  // Keyはインデントレベル。インデントが上がったら
                            int currentLevel = 0;

                            foreach(string line in str.Split('\n'))
                            {
                                int level = line.Split(' ')[0].Length;

                                if(level < currentLevel)
                                {
                                    symbolCount.Add(currentLevel, 0);
                                }

                                currentLevel = level;
                                if (!symbolCount.ContainsKey(currentLevel))
                                {
                                    symbolCount.Add(currentLevel, 1);
                                }
                                else
                                {
                                    symbolCount[currentLevel]++;
                                }

                                ret += textLevelSemanticsCheck(line.Replace("+ ", symbolCount[currentLevel].ToString() + ". ")) + "\n";
                            }

                            return ret;
                        };
                        p1 = lineFormat.Invoke(p1);

                        // ネストがあるときに残っている + をスペースに変換
                        var indentReplacer = new RegexMatchReplacer()
                        {
                            RegexOptions = RegexOptions.Multiline,
                            Pattern = "^(?<p4>\\++)(?<p5>.*)",
                            Formatter = m =>
                            {
                                var p4 = m.Groups["p4"].Value;
                                var p5 = m.Groups["p5"].Value;

                                int max = p4.Length;
                                string indent = "";

                                for(var i = 0;i < max; i++)
                                {
                                    indent += "    ";
                                }

                                return indent + p5;
                            }
                        };
                        p1 = indentReplacer.Replace(p1);

                        var result = $"\n{p1}\n";
                        return result;
                    }
                },

                // 非順序リスト
                new RegexMatchReplacer()
                {
                    RegexOptions = RegexOptions.Multiline,
                    Pattern = "^(?<p1>-+)(?<p2>.*)$",
                    Formatter = match =>
                    {
                        var p1 = match.Groups["p1"].Value;
                        var p2 = match.Groups["p2"].Value;

                        int i = 0;
                        int max = p1.Length - 1;
                        string indent = "";

                        if (string.IsNullOrEmpty(p2))
                        {
                            return p1;  // hr要素
                        }

                        for(i = 0;i < max; i++)
                        {
                            indent += "    ";
                        }

                        indent += "-";
                        p2 = textLevelSemanticsCheck(p2).Trim();

                        var result = $"{indent} {p2}";
                        return result;
                    }
                },


                // 改行をbr要素に変換（Markdown in Backlogでは無視されます）
                new RegexReplacer("&br;", " <br>"),
                new RegexReplacer("&", "&amp;")

            };
        }

        #endregion

        /// <summary>
        /// １行修飾を変換する
        /// </summary>
        /// <param name="src"></param>
        /// <returns></returns>
        static string textLevelSemanticsCheck(string src)
        {
            string result = src;

            foreach (var rep in textLevelSemanticsPattern)
            {
                result = rep.Replace(result);
            }

            return result;
        }

        /// <summary>
        /// 改行コードを変換する
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        string convertLf(string input)
        {
            string result = input;

            if (this.UseCrLfInResult)
            {
                result = result.Replace("\n", "\r\n");
            }

            return result;
        }

        #region 正規表現置換用クラス定義

        private interface IReplacer
        {
            string Pattern { get; }
            string Replace(string src);
        }

        /// <summary>
        /// 正規表現による置換を設定しておくクラス
        /// </summary>
        private class RegexReplacer : IReplacer
        {
            public string Pattern { get; }

            public string Format { get; }

            public RegexReplacer(string pattern, string replace)
            {
                this.Pattern = pattern;
                this.Format = replace;
            }

            public string Replace(string src)
            {
                var result = Regex.Replace(src, Pattern, Format);

                return result;
            }
        }

        /// <summary>
        /// 正規表現でマッチした結果をフォーマットして置換できるクラス
        /// </summary>
        private class RegexMatchReplacer : IReplacer
        {
            public string Pattern { get; set; }

            /// <summary>
            /// 正規表現の取得結果から新規文字列を整形するメソッド
            /// </summary>
            public MatchEvaluator Formatter { get; set; }

            /// <summary>
            /// 正規表現オプション
            /// </summary>
            public RegexOptions RegexOptions { get; set; }

            public RegexMatchReplacer()
            {
                RegexOptions = RegexOptions.None;
            }

            public string Replace(string src)
            {
                Regex reg = new Regex(Pattern, RegexOptions);
                string result = reg.Replace(src, Formatter);
                return result;
            }
        }

        #endregion
    }
}
