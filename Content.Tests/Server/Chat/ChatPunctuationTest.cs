using Content.Server.Chat.Systems;
using NUnit.Framework;

namespace Content.Tests.Server.Chat;

[TestFixture]
public sealed class ChatPunctuationTest
{
    [TestCase("Привет", "Привет.")]
    [TestCase("Привет   ", "Привет.")]
    [TestCase("Мне 25", "Мне 25.")]
    [TestCase("42", "42.")]
    [TestCase("Hola", "Hola.")]
    [TestCase("Привет!", "Привет!")]
    [TestCase("Ты где?", "Ты где?")]
    [TestCase("Ну...", "Ну...")]
    [TestCase("Ну…", "Ну…")]
    [TestCase("Я хотел—", "Я хотел—")]
    [TestCase("Я хот-", "Я хот-")]
    [TestCase("Он сказал «стой!»", "Он сказал «стой!»")]
    [TestCase("Он сказал «привет»", "Он сказал «привет».")]
    [TestCase("Привет (тихо)", "Привет (тихо).")]
    [TestCase("(Ты здесь?)", "(Ты здесь?)")]
    [TestCase("\"Привет\"", "\"Привет\".")]
    [TestCase("\"Привет!\"", "\"Привет!\"")]
    [TestCase("", "")]
    [TestCase("   ", "")]
    [TestCase(":)", ":)")]
    [TestCase(")))", ")))")]
    [TestCase("♥", "♥")]
    public void AddsOnlyMissingPunctuation(string input, string expected)
    {
        var actual = ChatSystem.SanitizeMessagePeriod(input);
        Assert.That(actual, Is.EqualTo(expected));
        Assert.That(ChatSystem.SanitizeMessagePeriod(actual), Is.EqualTo(actual));
    }
}
