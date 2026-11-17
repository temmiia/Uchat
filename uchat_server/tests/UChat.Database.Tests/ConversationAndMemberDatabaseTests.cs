using Microsoft.EntityFrameworkCore;
using UChat.Domain.Entities;
using UChat.Domain.Enums;

namespace UChat.Database.Tests;

public class ConversationAndMemberDatabaseTests
{
    [Fact]
    public async Task Can_Create_Conversation_And_Add_Members_With_Roles()
    {
        // Создаем групповой чат и добавляем участников с ролями Owner и Member
        var (context, connection) = TestDbContextFactory.CreateInMemoryDbContext();
        await using var _ = connection;
        await using var __ = context;

        var owner = new User("acc_owner", "owner_nick", "Owner User", "hash");
        var member = new User("acc_member", "member_nick", "Regular Member", "hash");
        context.Users.AddRange(owner, member);

        var conversation = new Conversation(ConversationType.Group, "Design Team Chat");
        context.Conversations.Add(conversation);

        var ownerMembership = new ConversationMember(conversation.ConversationId, owner.AccountId, MemberRole.Owner);
        var regularMembership = new ConversationMember(conversation.ConversationId, member.AccountId, MemberRole.Member);
        context.Members.AddRange(ownerMembership, regularMembership);

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var retrievedConversation = await context.Conversations
            .Include(c => c.Members)
            .ThenInclude(m => m.User)
            .FirstOrDefaultAsync(c => c.ConversationId == conversation.ConversationId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(retrievedConversation);
        Assert.Equal("Design Team Chat", retrievedConversation.Title);
        Assert.Equal(ConversationType.Group, retrievedConversation.Type);
        Assert.Equal(2, retrievedConversation.Members.Count);
        Assert.Contains(retrievedConversation.Members, m => m.AccountId == "acc_owner" && m.Role == MemberRole.Owner);
        Assert.Contains(retrievedConversation.Members, m => m.AccountId == "acc_member" && m.Role == MemberRole.Member);
    }

    [Fact]
    public async Task Duplicate_User_In_Same_Conversation_Throws_DbUpdateException()
    {
        // Проверяем, что один и тот же юзер не может быть добавлен в одну беседу дважды
        var (context, connection) = TestDbContextFactory.CreateInMemoryDbContext();
        await using var _ = connection;
        await using var __ = context;

        var user = new User("acc_dup_test", "unique_nick", "Test User", "hash");
        var conversation = new Conversation(ConversationType.Direct);
        context.Users.Add(user);
        context.Conversations.Add(conversation);

        var member1 = new ConversationMember(conversation.ConversationId, user.AccountId, MemberRole.Member);
        var member2 = new ConversationMember(conversation.ConversationId, user.AccountId, MemberRole.Member);

        context.Members.Add(member1);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        context.Members.Add(member2);

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
    }
}
