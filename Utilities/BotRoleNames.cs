namespace MintChatManager;

internal static class BotRoleNames
{
    public static string GetDisplayName(decimal role)
    {
        if (role >= 12) return "Владелец чат-менеджера";
        if (role == 11) return "Зам. Владельца чат-менеджера";
        if (role == 10.5m) return "Главный Руководитель";
        if (role == 10) return "Руководитель чат-менеджера";
        if (role == 9) return "Осн. Зам. Руководителя";
        if (role == 8) return "Зам. Руководителя";
        if (role == 7.6m) return "Главный Агент Поддержки Чат-менеджера";
        if (role == 7.5m) return "Зам. Главного Агента Поддержки Чат-менеджера";
        if (role == 7.4m) return "Агент Поддержки Чат-менеджера";
        if (role == 7.3m) return "Главный тех. специалист";
        if (role == 7.2m) return "Куратор тех. специалистов";
        if (role == 7.1m) return "Тех. Специалист";
        if (role >= 6) return "Владелец беседы";
        if (role == 5) return "Руководитель беседы";
        if (role == 4) return "Главный Администратор";
        if (role == 3) return "Администратор";
        if (role == 2) return "Старший Модератор";
        if (role == 1) return "Модератор";
        return "Пользователь";
    }
}
