// Copyright (c) 2026 eirros. All rights reserved.
using System;
using System.Collections.Generic;

internal static class LanRoomText
{
    private static readonly string[] Locales = { "zh-Hans", "zh-Hant", "en-US", "ja-JP", "ko-KR" };
    private static readonly Dictionary<string, string[]> Text = new Dictionary<string, string[]>
    {
        { "account_new", new[]{ "新建本地账号", "新增本機帳號", "New account", "アカウント作成", "로컬 계정 만들기" } },
        { "account_save", new[]{ "保存账号", "儲存帳號", "Save account", "アカウントを保存", "계정 저장" } },
        { "account_back", new[]{ "返回账号列表", "返回帳號清單", "Back to accounts", "アカウント一覧に戻る", "계정 목록으로" } },
        { "account_play_solo", new[]{ "开始单人游戏", "開始單人遊戲", "Play solo", "ソロで遊ぶ", "혼자 플레이" } },
        { "account_saved", new[]{ "账号已保存。现在可以开始单人游戏或创建局域网房间。", "帳號已儲存。現在可開始單人遊戲或建立區域網路房間。",
            "Account saved. You can now play solo or host a LAN game.", "アカウントを保存しました。ソロで遊ぶか、LANルームを作れます。", "계정을 저장했습니다. 혼자 플레이하거나 LAN 방을 열 수 있습니다." } },
        { "launcher_lan_host", new[]{ "创建局域网房间", "開設區網房間", "Host LAN game", "LANルームを作る", "LAN 방 만들기" } },
        { "launcher_lan_join", new[]{ "加入局域网房间", "加入區網房間", "Join LAN game", "LANルームに参加", "LAN 방 참가" } },
        { "lan_room", new[]{ "局域网房间", "區域網路房間", "LAN room", "LANルーム", "LAN 방" } },
        { "lan_join", new[]{ "加入房间", "加入房間", "Join room", "ルームに参加", "방 참가" } },
        { "lan_connect_play", new[]{ "连接房间并进入游戏", "連線並進入遊戲", "Connect and play", "接続してプレイ", "접속하여 플레이" } },
        { "lan_invite", new[]{ "房间邀请", "房間邀請", "Room invitation", "招待コード", "방 초대 코드" } },
        { "lan_name", new[]{ "玩家名称", "玩家名稱", "Player name", "プレイヤー名", "플레이어 이름" } },
        { "lan_open", new[]{ "开放房间", "開放房間", "Open room", "ルームを開く", "방 열기" } },
        { "lan_creating", new[]{ "正在创建局域网房间…", "正在建立區域網路房間…", "Creating LAN room…", "LANルームを作成中…", "LAN 방을 만드는 중…" } },
        { "lan_enter_account", new[]{ "请先填写账号名称，再创建房间。", "請先填寫帳號名稱，再建立房間。", "Enter an account name to create a room.",
            "アカウント名を入力してからルームを作成してください。", "계정 이름을 입력한 뒤 방을 만들어 주세요." } },
        { "lan_close", new[]{ "关闭房间", "關閉房間", "Close room", "ルームを閉じる", "방 닫기" } },
        { "lan_copy", new[]{ "复制邀请", "複製邀請", "Copy invitation", "招待をコピー", "초대 복사" } },
        { "lan_host_guide", new[]{ "把邀请发给同一局域网的朋友。来宾角色保存在这台电脑上。",
            "把邀請傳給同一區域網路的朋友。訪客角色保存在這台電腦上。", "Share the invitation on your local network. Guest characters are saved on this PC.",
            "同じLANの友達に招待を送ってください。ゲストのキャラクターはこのPCに保存されます。", "같은 LAN의 친구에게 초대를 보내세요. 게스트 캐릭터는 이 PC에 저장됩니다。" } },
        { "lan_join_guide", new[]{ "粘贴房主发来的邀请。用相同的玩家名称可以找回在这个房间的角色。",
            "貼上房主傳來的邀請。使用同一個玩家名稱可找回這個房間的角色。", "Paste the host's invitation. Use the same player name on this PC to load your characters.",
            "ホストの招待を貼り付けてください。同じPCで同じ名前を使うとキャラクターを引き継げます。", "방장의 초대를 붙여 넣으세요. 이 PC에서 같은 이름을 쓰면 캐릭터를 다시 불러옵니다." } },
        { "lan_address", new[]{ "本机局域网地址", "本機區域網路位址", "Local network address", "LANアドレス", "로컬 네트워크 주소" } },
        { "lan_room_address", new[]{ "房间地址", "房間位址", "Room address", "ルームのアドレス", "방 주소" } },
        { "lan_select_network", new[]{ "选择与朋友共用的网络，再创建房间。", "選擇與朋友共用的網路，再建立房間。", "Choose the network you share with your friends.",
            "友達と同じネットワークを選んでください。", "친구와 같은 네트워크를 선택하세요." } },
        { "lan_offline", new[]{ "房间尚未开放", "房間尚未開放", "Room closed", "ルームは閉じています", "방이 닫혀 있습니다" } },
        { "lan_opened", new[]{ "房间已开放", "房間已開放", "Room open", "ルーム公開中", "방이 열렸습니다" } },
        { "lan_players", new[]{ "已连接来宾 · {0} 人", "已連線訪客 · {0} 人", "Guests connected · {0}",
            "接続中のゲスト · {0} 人", "연결된 게스트 · {0}명" } },
        { "lan_copied", new[]{ "已复制房间邀请", "已複製房間邀請", "Invitation copied", "招待をコピーしました", "초대 코드를 복사했습니다" } },
        { "lan_wait", new[]{ "正在更新房间…", "正在更新房間…", "Updating room…", "ルームを更新中…", "방을 업데이트하는 중…" } },
        { "lan_game_required", new[]{ "请进入本机游戏后再开放房间。", "請進入本機遊戲後再開放房間。", "Start a local game before opening a room.",
            "ローカルゲームを起動してからルームを開いてください。", "로컬 게임을 시작한 뒤 방을 열어 주세요." } },
        { "lan_invalid_address", new[]{ "没有可用的局域网 IPv4 地址。", "沒有可用的區域網路 IPv4 位址。", "No usable local IPv4 address.",
            "利用できるLANのIPv4アドレスがありません。", "사용 가능한 LAN IPv4 주소가 없습니다." } },
        { "lan_invalid_invite", new[]{ "请粘贴完整的房间邀请。", "請貼上完整的房間邀請。", "Paste the full room invitation.",
            "招待コード全体を貼り付けてください。", "초대 코드 전체를 붙여 넣어 주세요." } },
        { "lan_invalid_name", new[]{ "玩家名称需为 1～24 个可见字符。", "玩家名稱需為 1～24 個可見字元。", "Enter a player name of 1–24 visible characters.",
            "プレイヤー名を1～24文字で入力してください。", "플레이어 이름을 1~24자로 입력해 주세요." } },
        { "lan_open_failed", new[]{ "房间未能开放。请检查本机地址或重新进入游戏。", "房間未能開放。請檢查本機位址或重新進入遊戲。", "Could not open the room. Check the local address or restart the game.",
            "ルームを開けません。LANアドレスを確認するか、ゲームを再起動してください。", "방을 열 수 없습니다. 네트워크 주소를 확인하거나 게임을 다시 시작해 주세요." } },
        { "lan_incompatible", new[]{ "房主与来宾需要使用同一个联机版本。", "房主與訪客需要使用同一個連線版本。", "The host and guest need the same LAN version.",
            "ホストとゲストは同じLAN対応バージョンを使用してください。", "방장과 게스트가 같은 LAN 버전을 사용해야 합니다." } },
        { "lan_unreachable", new[]{ "连不上房间。请确认房间仍开放，并且两台电脑在同一局域网。", "連不上房間。請確認房間仍開放，並且兩台電腦在同一區域網路。",
            "Could not reach the room. Check that it is open and both PCs are on the same LAN.", "接続できません。ルームが開いていて、両方のPCが同じLANにあることを確認してください。",
            "방에 연결할 수 없습니다. 방이 열려 있고 두 PC가 같은 LAN에 있는지 확인해 주세요." } },
        { "lan_invite_expired", new[]{ "邀请已失效，请向房主获取最新邀请。", "邀請已失效，請向房主取得最新邀請。", "Invitation expired. Ask the host for a fresh invitation.",
            "招待は無効です。ホストに新しい招待を依頼してください。", "초대가 만료되었습니다. 새 초대 코드를 받아 주세요." } },
        { "lan_certificate_changed", new[]{ "房间身份已变化，请向房主获取最新邀请。", "房間身分已變更，請向房主取得最新邀請。", "The room identity changed. Ask the host for a fresh invitation.",
            "ルームの識別情報が変わりました。ホストに新しい招待を依頼してください。", "방이 변경되었습니다. 새 초대 코드를 받아 주세요." } },
        { "lan_guest_account_error", new[]{ "来宾账号未能载入，请让房主检查存档。", "訪客帳號未能載入，請讓房主檢查存檔。", "Could not load the guest account. Ask the host to check the save.",
            "ゲストアカウントを読み込めません。ホストにセーブデータの確認を依頼してください。", "게스트 계정을 불러올 수 없습니다. 방장에게 저장 데이터를 확인해 달라고 요청하세요." } },
        { "lan_room_busy", new[]{ "房间已满，或该玩家已连接。", "房間已滿，或該玩家已連線。", "The room is full or this player is already connected.",
            "ルームが満員か、プレイヤーが既に接続しています。", "방이 가득 찼거나 이미 접속한 플레이어입니다." } },
        { "lan_channel_closed", new[]{ "这个频道已关闭。", "這個頻道已關閉。", "This channel is closed.", "このチャンネルは閉じています。", "이 채널은 닫혀 있습니다." } },
        { "lan_local_port_busy", new[]{ "请关闭这台电脑上的游戏和本机服务，再加入房间。", "請關閉這台電腦上的遊戲和本機服務，再加入房間。",
            "Close the game and its local services on this PC before joining.", "このPCのゲームとローカルサービスを終了してから参加してください。",
            "이 PC의 게임과 로컬 서비스를 닫은 뒤 참가해 주세요." } },
        { "lan_game_start_failed", new[]{ "已连接房间，但游戏未能启动。", "已連線至房間，但遊戲未能啟動。", "Connected to the room, but the game did not start.",
            "ルームに接続しましたが、ゲームを起動できませんでした。", "방에 연결했지만 게임을 시작할 수 없습니다." } },
        { "lan_connecting", new[]{ "正在连接房间…", "正在連線至房間…", "Connecting to room…", "ルームに接続中…", "방에 연결하는 중…" } },
        { "lan_starting", new[]{ "已连接，正在启动游戏…", "已連線，正在啟動遊戲…", "Connected. Starting the game…", "接続しました。ゲームを起動中…", "연결되었습니다. 게임을 시작하는 중…" } },
    };

    internal static string Get(string key, string locale)
    {
        string[] values;
        if (!Text.TryGetValue(key, out values)) return key;
        return values[Math.Max(0, Array.IndexOf(Locales, locale))];
    }
}
