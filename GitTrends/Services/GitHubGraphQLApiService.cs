﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using GitTrends.Mobile.Shared;
using GitTrends.Shared;
using Refit;

namespace GitTrends
{
    public class GitHubGraphQLApiService : BaseMobileApiService
    {
        readonly static Lazy<IGitHubGraphQLApi> _githubApiClientHolder = new Lazy<IGitHubGraphQLApi>(() => RestService.For<IGitHubGraphQLApi>(CreateHttpClient(GitHubConstants.GitHubGraphQLApi)));

        static IGitHubGraphQLApi GitHubApiClient => _githubApiClientHolder.Value;

        public async Task<(string login, string name, Uri avatarUri)> GetCurrentUserInfo()
        {
            var token = await GitHubAuthenticationService.GetGitHubToken().ConfigureAwait(false);
            var data = await ExecuteGraphQLRequest(() => GitHubApiClient.ViewerLoginQuery(new ViewerLoginQueryContent(), GetGitHubBearerTokenHeader(token))).ConfigureAwait(false);

            return (data.Viewer.Alias, data.Viewer.Name, data.Viewer.AvatarUri);
        }

        public async Task<User> GetUser(string username)
        {
            var token = await GitHubAuthenticationService.GetGitHubToken().ConfigureAwait(false);
            var data = await ExecuteGraphQLRequest(() => GitHubApiClient.UserQuery(new UserQueryContent(username), GetGitHubBearerTokenHeader(token))).ConfigureAwait(false);

            return data.User;
        }

        public async Task<Repository> GetRepository(string repositoryOwner, string repositoryName, int numberOfIssuesPerRequest = 100)
        {
            var token = await GitHubAuthenticationService.GetGitHubToken().ConfigureAwait(false);
            var data = await ExecuteGraphQLRequest(() => GitHubApiClient.RepositoryQuery(new RepositoryQueryContent(repositoryOwner, repositoryName, numberOfIssuesPerRequest), GetGitHubBearerTokenHeader(token))).ConfigureAwait(false);

            return data.Repository;
        }

        public async IAsyncEnumerable<IEnumerable<Repository>> GetRepositories(string repositoryOwner, int numberOfRepositoriesPerRequest = 100)
        {
            if (GitHubAuthenticationService.IsDemoUser)
            {
                //Yield off of main thread to generate the demoDataList
                await Task.Yield();

                var demoDataList = new List<Repository>();

                for (int i = 0; i < DemoDataConstants.RepoCount; i++)
                {
                    var demoRepo = new Repository($"Repository " + DemoDataConstants.GetRandomText(), DemoDataConstants.GetRandomText(), DemoDataConstants.GetRandomNumber(),
                                                new RepositoryOwner(DemoDataConstants.Alias, DemoDataConstants.AvatarUrl),
                                                new IssuesConnection(DemoDataConstants.GetRandomNumber(), Enumerable.Empty<Issue>()),
                                                DemoDataConstants.AvatarUrl, new StarGazers(DemoDataConstants.GetRandomNumber()), false);
                    demoDataList.Add(demoRepo);
                }

                yield return demoDataList;

                //Allow UI to update
                await Task.Delay(1000).ConfigureAwait(false);
            }
            else
            {
                RepositoryConnection? repositoryConnection = null;

                do
                {
                    repositoryConnection = await GetRepositoryConnection(repositoryOwner, repositoryConnection?.PageInfo?.EndCursor, numberOfRepositoriesPerRequest).ConfigureAwait(false);
                    yield return repositoryConnection?.RepositoryList ?? Enumerable.Empty<Repository>();
                }
                while (repositoryConnection?.PageInfo?.HasNextPage is true);
            }
        }

        async Task<RepositoryConnection> GetRepositoryConnection(string repositoryOwner, string? endCursor, int numberOfRepositoriesPerRequest = 100)
        {
            var token = await GitHubAuthenticationService.GetGitHubToken().ConfigureAwait(false);
            var data = await ExecuteGraphQLRequest(() => GitHubApiClient.RepositoryConnectionQuery(new RepositoryConnectionQueryContent(repositoryOwner, GetEndCursorString(endCursor), numberOfRepositoriesPerRequest), GetGitHubBearerTokenHeader(token))).ConfigureAwait(false);

            return data.GitHubUser.RepositoryConnection;
        }

        async Task<T> ExecuteGraphQLRequest<T>(Func<Task<GraphQLResponse<T>>> action, int numRetries = 2, [CallerMemberName] string callerName = "")
        {
            var response = await AttemptAndRetry_Mobile(action, numRetries, callerName: callerName).ConfigureAwait(false);

            if (response.Errors != null)
                throw new AggregateException(response.Errors.Select(x => new Exception(x.Message)));

            return response.Data;
        }

        string GetEndCursorString(string? endCursor) => string.IsNullOrWhiteSpace(endCursor) ? string.Empty : "after: \"" + endCursor + "\"";
    }
}
