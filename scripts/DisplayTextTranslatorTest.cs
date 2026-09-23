using System;
using System.IO;
using RO3.JapaneseMod;

class DisplayTextTranslatorTest
{
    static int Main(string[] args)
    {
        var translator = new DisplayTextTranslator();
        foreach (string line in File.ReadAllLines(args[0]))
        {
            string[] fields = line.TrimStart('\uFEFF').Split(new[] { '\t' }, 3);
            if (fields.Length == 3) translator.Add(fields[0], fields[1], fields[2]);
        }
        string[,] cases = {
            { "Goblin Archer", "ゴブリンアーチャー" },
            { "Magnus Exorcismus!!", "マグヌスエクソシズム!!" },
            { "Take part in events and enjoy your adventures in this world", "イベントに参加して、この世界での冒険を楽しもう" },
            { "<color=#FF0000>Goblin Archer</color>", "<color=#FF0000>ゴブリンアーチャー</color>" },
            { "Current Server Level Cap: Lv. 69\n09/24/2026 05:00:00: Server Level Cap increases to Lv. 79", "現在のサーバーレベル上限：Lv.69\n09/24/2026 05:00:00：サーバーレベル上限がLv.79に上昇" },
            { "<color=#99FF9F>Current Server Level Cap: Lv. 69\n09/24/2026 05:00:00: Server Level Cap increases to Lv. 79</color>", "<color=#99FF9F>現在のサーバーレベル上限：Lv.69\n09/24/2026 05:00:00：サーバーレベル上限がLv.79に上昇</color>" },
            { "mikosurihan", "mikosurihan" },
            { "ゴブリンアーチャー", "ゴブリンアーチャー" },
            { "Current Server Level Cap: unexpected format", "Current Server Level Cap: unexpected format" },
            { "Take part in events and enjoy your adventures in this world\n<color=#99FF9F>Current Server Level Cap: Lv. 69\n09/24/2026 05:00:00: サーバーレベル上限がLv.79に上昇</color>", "イベントに参加して、この世界での冒険を楽しもう\n<color=#99FF9F>現在のサーバーレベル上限：Lv.69\n09/24/2026 05:00:00: サーバーレベル上限がLv.79に上昇</color>" },
            { "", "" },
            { null, null }
        };
        int failed = 0;
        for (int i = 0; i < cases.GetLength(0); i++)
        {
            string actual = translator.Translate(cases[i, 0]);
            if (actual != cases[i, 1] || translator.Translate(actual) != actual)
            {
                Console.WriteLine("FAILED: display case " + i);
                failed++;
            }
        }
        Console.WriteLine("Tests: " + (cases.GetLength(0) - failed) + "/" + cases.GetLength(0) + " passed");
        return failed == 0 ? 0 : 1;
    }
}
