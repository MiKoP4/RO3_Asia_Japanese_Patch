using System;
using System.IO;
using RO3.JapaneseMod;

class DisplayTextTranslatorTest
{
    class GetterTranslatedText
    {
        private string m_text;
        public GetterTranslatedText(string raw) { m_text = raw; }
        public string text { get { return "Getter already translated"; } }
    }
    class BufferedText : GetterTranslatedText
    {
        private bool m_IsTextBackingStringDirty;
        private readonly string buffer;
        public BufferedText(string raw, string current, bool dirty) : base(raw)
        { buffer = current; m_IsTextBackingStringDirty = dirty; }
        private string InternalTextBackingArrayToString() { return buffer; }
    }
    class LegacyText
    {
        private string m_Text = "Legacy raw";
        public string text { get { return "Getter already translated"; } }
    }
    class OtherText { public string text { get { return "Fallback raw"; } } }

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
            { "プレイヤー【TestPlayer】が【Eddga】を撃破し、レア報酬を獲得しました！", "プレイヤー【TestPlayer】が【エドガ】を撃破し、レア報酬を獲得しました！" },
            { "Moonlight FlowerはTestPlayerに倒されました。", "月夜花はTestPlayerに倒されました。" },
            { "Moonlight Flower was defeated by TestPlayer.", "月夜花はTestPlayerに倒されました。" },
            { "<color=#fdab5b>Moonlight Flower</color>は<color=#fdab5b>TestPlayer</color>に倒されました。", "<color=#fdab5b>月夜花</color>は<color=#fdab5b>TestPlayer</color>に倒されました。" },
            { "EddgaはUnlistedPlayerに倒されました。", "エドガはUnlistedPlayerに倒されました。" },
            { "UnlistedBossはEddgaに倒されました。", "UnlistedBossはEddgaに倒されました。" },
            { "プレイヤー【<color=#fdab5b>TestPlayer</color>】が【<color=#fdab5b>Eddga</color>】を撃破し、レア報酬を獲得しました！", "プレイヤー【<color=#fdab5b>TestPlayer</color>】が【<color=#fdab5b>エドガ</color>】を撃破し、レア報酬を獲得しました！" },
            { "Congratulations to player 【TestPlayer】 for defeating 【Baphomet】 and earning a Rare reward!", "プレイヤー【TestPlayer】が【バフォメット】を撃破し、レア報酬を獲得しました！" },
            { "Congratulations to player 【<color=#fdab5b>TestPlayer</color>】 for defeating 【<color=#fdab5b>Eddga</color>】 and earning a Rare reward!", "プレイヤー【<color=#fdab5b>TestPlayer</color>】が【<color=#fdab5b>エドガ</color>】を撃破し、レア報酬を獲得しました！" },
            { "プレイヤー【Eddga】が【UnlistedBoss】を撃破し、レア報酬を獲得しました！", "プレイヤー【Eddga】が【UnlistedBoss】を撃破し、レア報酬を獲得しました！" },
            { "Magnus Exorcismus!!", "マグヌスエクソシズム!!" },
            { "Take part in events and enjoy your adventures in this world", "イベントに参加して、この世界での冒険を楽しもう" },
            { "<color=#FF0000>Goblin Archer</color>", "<color=#FF0000>ゴブリンアーチャー</color>" },
            { "Current Server Level Cap: Lv. 69\n09/24/2026 05:00:00: Server Level Cap increases to Lv. 79", "現在のサーバーレベル上限：Lv.69\n09/24/2026 05:00:00：サーバーレベル上限がLv.79に上昇" },
            { "<color=#99FF9F>Current Server Level Cap: Lv. 69\n09/24/2026 05:00:00: Server Level Cap increases to Lv. 79</color>", "<color=#99FF9F>現在のサーバーレベル上限：Lv.69\n09/24/2026 05:00:00：サーバーレベル上限がLv.79に上昇</color>" },
            { "Monsters Unlocked: 136/162", "解放モンスター：136/162" },
            { "Dedicated Scholar-2", "熱心な学者-2" },
            { "Complete the Commission: 0/1", "依頼を完了：0/1" },
            { "Kill MVPs: 0/5", "MVP討伐数: 0/5" },
            { "Join the Caravan: 0/1", "キャラバンに参加：0/1" },
            { "High-Reward Auto Mode: 60/60", "高報酬オートモード: 60/60" },
            { "Consume Vigor: 0/100", "活力消費：0/100" },
            { "Take on a squad Challenge against the Phantom Realm BOSS with 5 players to earn tons of rewards.", "5人でファントムレルムBOSSに挑むチームチャレンジに参加し、大量の報酬を獲得しましょう。" },
            { "Take on a Party Challenge against the Realm of the Gods BOSS with 10 players to earn tons of rewards.", "10人で神域BOSSに挑むパーティチャレンジに参加し、大量の報酬を獲得しましょう。" },
            { "Consumed when gathering and crafting with a Life Skill. Obtain it from Recommend-Activity Chests. You can accumulate up to 5000 points.", "生活スキルでの採取や製作時に消費される。おすすめアクティビティ宝箱から入手可能。最大5000ポイントまで累積できる。" },
            { "Consumed when gathering and crafting with a Life Skill. Obtain it from Recommend-Activity Chests. You can accumulate up to <color=#ff993f>5000</color> points.", "生活スキルでの採取や製作時に消費される。おすすめアクティビティ宝箱から入手可能。最大<color=#ff993f>5000</color>ポイントまで累積できる。" },
            { "A vibrant Green Healing Potion that instantly restores 1500 HP. Cooldown: 40 sec.", "鮮やかな緑の回復ポーション。HPを即座に1500回復。クールダウン：40秒。" },
            { "MATK +22", "魔法攻撃 +22" },
            { "Magic Damage Increase +0.23%", "魔法ダメージ増加 +0.23%" },
            { "MATK Increase", "MATK増加" },
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
        string[,] backingCases = {
            { DisplayTextBackingStore.Read(new GetterTranslatedText("Goblin Archer")), "Goblin Archer" },
            { DisplayTextBackingStore.Read(new GetterTranslatedText("Second instance")), "Second instance" },
            { DisplayTextBackingStore.Read(new BufferedText("Stale", "Current buffer", true)), "Current buffer" },
            { DisplayTextBackingStore.Read(new BufferedText("Current string", "Stale buffer", false)), "Current string" },
            { DisplayTextBackingStore.Read(new BufferedText("Stale", "", true)), "" },
            { DisplayTextBackingStore.Read(new LegacyText()), "Legacy raw" },
            { DisplayTextBackingStore.Read(new OtherText()), "Fallback raw" },
            { DisplayTextBackingStore.Read(null), null },
        };
        for (int i = 0; i < backingCases.GetLength(0); i++)
            if (backingCases[i, 0] != backingCases[i, 1]) { Console.WriteLine("FAILED: backing text case " + i); failed++; }
        translator.AddOfflineExact("None", "なし");
        string[] rankPath = { "Team_TTxt", "TeamType_TTxt", "Layout_Info", "Info", "Top_GraphicSwitchG", "TipsRoot_RTransform" };
        string[,] rankCases = {
            { "无", "なし" }, { "無", "なし" },
            { "<color=#7e7361>无</color>", "<color=#7e7361>なし</color>" },
            { "无題", "无題" }, { "TestPlayer", "TestPlayer" }, { "なし", "なし" },
        };
        for (int i = 0; i < rankCases.GetLength(0); i++)
            if (translator.TranslateProfileRankValue(rankCases[i, 0], rankPath) != rankCases[i, 1])
            { Console.WriteLine("FAILED: scoped rank case " + i); failed++; }
        for (int i = 0; i < rankPath.Length; i++)
        {
            string[] otherSlot = (string[])rankPath.Clone();
            otherSlot[i] = "OtherField";
            if (translator.TranslateProfileRankValue("无", otherSlot) != "无")
            { Console.WriteLine("FAILED: unrelated rank slot " + i); failed++; }
        }
        int total = cases.GetLength(0) + backingCases.GetLength(0) + rankCases.GetLength(0) + rankPath.Length;
        Console.WriteLine("Tests: " + (total - failed) + "/" + total + " passed");
        return failed == 0 ? 0 : 1;
    }
}
