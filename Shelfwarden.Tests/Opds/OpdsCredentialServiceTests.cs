using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shelfwarden.Models;
using Shelfwarden.Services.Auth;
using Shelfwarden.Services.Opds;
using Shelfwarden.Tests.Infrastructure;

namespace Shelfwarden.Tests.Opds;

/// <summary>
/// OPDS keys stand in for passwords, so the rules that matter are: only a hash is stored, a key
/// stops working as soon as it's replaced or revoked, it only works with its owner's user name,
/// and (under Identity) a disabled or deleted account can't keep using it.
/// </summary>
public class OpdsCredentialServiceTests : IClassFixture<TestDbFixture>
{
    private readonly TestDbFixture fixture;

    public OpdsCredentialServiceTests(TestDbFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task Generated_keys_are_readable_and_only_their_hash_is_stored()
    {
        using var scope = fixture.CreateScope();
        var user = NewUser();
        var service = new Harness(scope, user).Service;

        var issued = (await service.GenerateAsync()).Value;

        Assert.Matches("^[0-9a-hjkmnp-tv-z]{5}(-[0-9a-hjkmnp-tv-z]{5}){3}$", issued.Key);
        Assert.Equal(issued.Key.Replace("-", ""), issued.CompactKey);
        Assert.Equal(issued.CompactKey[..5], issued.Credential.KeyPrefix);

        var stored = await scope.ServiceProvider.GetRequiredService<IRepository<OpdsCredential>>()
            .FindOneAsync(new SearchOptions<OpdsCredential> { Query = c => c.UserId == user.Id });
        Assert.NotNull(stored);
        Assert.Equal(OpdsCredentialService.HashKey(issued.CompactKey), stored.KeyHash);
        Assert.DoesNotContain(issued.CompactKey, stored.KeyHash);
    }

    [Fact]
    public async Task Keys_validate_with_the_owners_user_name_or_email_regardless_of_formatting()
    {
        using var scope = fixture.CreateScope();
        var user = NewUser();
        var service = new Harness(scope, user).Service;
        string key = (await service.GenerateAsync()).Value.Key;

        var result = await service.ValidateAsync(user.UserName.ToUpperInvariant(), key);
        Assert.True(result.IsSuccess);
        Assert.Equal(user.Id, result.Value.UserId);
        Assert.Equal(user.Roles, result.Value.Roles);

        Assert.True((await service.ValidateAsync(user.Email, key.Replace("-", "").ToUpperInvariant())).IsSuccess);
        Assert.True((await service.ValidateAsync($"  {user.UserName} ", $" {key} ")).IsSuccess);
    }

    [Fact]
    public async Task Keys_in_the_url_need_no_user_name()
    {
        using var scope = fixture.CreateScope();
        var user = NewUser();
        var service = new Harness(scope, user).Service;
        string key = (await service.GenerateAsync()).Value.CompactKey;

        Assert.True((await service.ValidateAsync(null, key)).IsSuccess);
    }

    [Fact]
    public async Task Another_users_name_or_a_wrong_key_is_rejected()
    {
        using var scope = fixture.CreateScope();
        var user = NewUser();
        var service = new Harness(scope, user).Service;
        string key = (await service.GenerateAsync()).Value.Key;

        Assert.Equal(ResultStatus.Unauthorized, (await service.ValidateAsync("someone-else", key)).Status);
        Assert.Equal(ResultStatus.Unauthorized, (await service.ValidateAsync(user.UserName, "00000-00000-00000-00000")).Status);
        Assert.Equal(ResultStatus.Unauthorized, (await service.ValidateAsync(user.UserName, key[..10])).Status);
        Assert.Equal(ResultStatus.Unauthorized, (await service.ValidateAsync(user.UserName, "")).Status);
    }

    [Fact]
    public async Task Regenerating_retires_the_old_key_immediately()
    {
        using var scope = fixture.CreateScope();
        var user = NewUser();
        var service = new Harness(scope, user).Service;
        string oldKey = (await service.GenerateAsync()).Value.Key;
        Assert.True((await service.ValidateAsync(user.UserName, oldKey)).IsSuccess);

        string newKey = (await service.GenerateAsync()).Value.Key;

        Assert.NotEqual(oldKey, newKey);
        Assert.Equal(ResultStatus.Unauthorized, (await service.ValidateAsync(user.UserName, oldKey)).Status);
        Assert.True((await service.ValidateAsync(user.UserName, newKey)).IsSuccess);
    }

    [Fact]
    public async Task Revoking_retires_the_key_and_is_idempotent()
    {
        using var scope = fixture.CreateScope();
        var user = NewUser();
        var service = new Harness(scope, user).Service;
        string key = (await service.GenerateAsync()).Value.Key;
        Assert.True((await service.ValidateAsync(user.UserName, key)).IsSuccess);

        Assert.True((await service.RevokeAsync()).IsSuccess);
        Assert.True((await service.RevokeAsync()).IsSuccess);

        Assert.Equal(ResultStatus.Unauthorized, (await service.ValidateAsync(user.UserName, key)).Status);
        Assert.Equal(ResultStatus.NotFound, (await service.GetCurrentAsync()).Status);
    }

    [Fact]
    public async Task Disabled_or_deleted_identity_accounts_cannot_use_their_key()
    {
        using var scope = fixture.CreateScope();
        var user = NewUser();
        var harness = new Harness(scope, user);
        string key = (await harness.Service.GenerateAsync()).Value.Key;

        harness.SetStoredUser(user with { IsDisabled = true });
        Assert.Equal(ResultStatus.Unauthorized, (await harness.Service.ValidateAsync(user.UserName, key)).Status);

        harness.SetStoredUser(null);
        Assert.Equal(ResultStatus.Unauthorized, (await harness.Service.ValidateAsync(null, key)).Status);
    }

    [Fact]
    public async Task Identity_role_changes_apply_to_existing_keys()
    {
        using var scope = fixture.CreateScope();
        var user = NewUser(Constants.Roles.Administrator);
        var harness = new Harness(scope, user);
        string key = (await harness.Service.GenerateAsync()).Value.Key;

        harness.SetStoredUser(user with { Roles = [Constants.Roles.User] });

        Assert.Equal([Constants.Roles.User], (await harness.Service.ValidateAsync(user.UserName, key)).Value.Roles);
    }

    [Fact]
    public async Task Without_a_local_user_store_the_session_snapshot_is_used_and_refreshed()
    {
        using var scope = fixture.CreateScope();
        var user = NewUser(Constants.Roles.Administrator);
        var harness = new Harness(scope, user, AuthProvider.Keycloak);
        string key = (await harness.Service.GenerateAsync()).Value.Key;

        Assert.Equal([Constants.Roles.Administrator], (await harness.Service.ValidateAsync(user.UserName, key)).Value.Roles);

        // A later visit to the OPDS page with fewer roles re-captures them.
        var demoted = new Harness(scope, user with { Roles = [Constants.Roles.User] }, AuthProvider.Keycloak);
        Assert.True((await demoted.Service.GetCurrentAsync()).IsSuccess);

        Assert.Equal([Constants.Roles.User], (await demoted.Service.ValidateAsync(user.UserName, key)).Value.Roles);
        harness.UserInfo.Verify(x => x.GetUserAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Only_administrators_can_list_or_revoke_other_users_keys()
    {
        using var scope = fixture.CreateScope();
        var member = NewUser();
        var memberService = new Harness(scope, member).Service;
        string memberKey = (await memberService.GenerateAsync()).Value.Key;

        Assert.Equal(ResultStatus.Forbidden, (await memberService.ListAllAsync()).Status);
        Assert.Equal(ResultStatus.Forbidden, (await memberService.RevokeForUserAsync(member.Id)).Status);

        var admin = new Harness(scope, NewUser(Constants.Roles.Administrator), cache: null, storedUsers: [member]);
        var all = (await admin.Service.ListAllAsync()).Value;
        Assert.Contains(all, c => c.UserId == member.Id && c.UserName == member.UserName);

        Assert.True((await admin.Service.RevokeForUserAsync(member.Id)).IsSuccess);
        Assert.Equal(ResultStatus.Unauthorized, (await admin.Service.ValidateAsync(member.UserName, memberKey)).Status);
    }

    [Fact]
    public async Task Signed_out_callers_cannot_manage_keys()
    {
        using var scope = fixture.CreateScope();
        var harness = new Harness(scope, NewUser(), signedIn: false);

        Assert.Equal(ResultStatus.Unauthorized, (await harness.Service.GenerateAsync()).Status);
        Assert.Equal(ResultStatus.Unauthorized, (await harness.Service.GetCurrentAsync()).Status);
        Assert.Equal(ResultStatus.Unauthorized, (await harness.Service.RevokeAsync()).Status);
    }

    private static UserInfo NewUser(string role = Constants.Roles.User)
    {
        string id = Guid.NewGuid().ToString("N");
        return new UserInfo(id, $"reader-{id[..8]}", $"reader-{id[..8]}@example.com", null, [role]);
    }

    /// <summary>A credential service acting as <paramref name="caller"/>, with a mocked user store.</summary>
    private sealed class Harness
    {
        private readonly Dictionary<string, UserInfo?> users = [];

        public Harness(
            IServiceScope scope,
            UserInfo caller,
            AuthProvider provider = AuthProvider.Identity,
            IMemoryCache? cache = null,
            bool signedIn = true,
            params UserInfo[] storedUsers)
        {
            users[caller.Id] = caller;
            foreach (var stored in storedUsers)
            {
                users[stored.Id] = stored;
            }

            var userContext = new Mock<IUserContextService>();
            userContext.Setup(x => x.GetCurrentUserId()).Returns(signedIn ? caller.Id : null);
            userContext.Setup(x => x.GetCurrentUserName()).Returns(signedIn ? caller.UserName : null);
            userContext.Setup(x => x.IsAuthenticated()).Returns(signedIn);
            userContext.Setup(x => x.IsAdministrator()).Returns(signedIn && caller.IsInRole(Constants.Roles.Administrator));
            userContext.Setup(x => x.GetRoleNames()).Returns(signedIn ? caller.Roles : []);

            var authProvider = new Mock<IAuthProviderService>();
            authProvider.Setup(x => x.Provider).Returns(provider);

            UserInfo.Setup(x => x.GetUserAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string id, CancellationToken _) => users.GetValueOrDefault(id));

            Service = new OpdsCredentialService(
                NullLogger<OpdsCredentialService>.Instance,
                userContext.Object,
                authProvider.Object,
                UserInfo.Object,
                cache ?? new MemoryCache(new MemoryCacheOptions()),
                TimeProvider.System,
                scope.ServiceProvider.GetRequiredService<IRepository<OpdsCredential>>());
        }

        public Mock<IUserInfoService> UserInfo { get; } = new();

        public OpdsCredentialService Service { get; }

        /// <summary>Changes what the user store returns for the caller; null simulates a deleted account.</summary>
        public void SetStoredUser(UserInfo? user)
        {
            string id = users.Keys.First();
            users[id] = user;
        }
    }
}
