namespace NetSquare.Server
{
    /// <summary>
    /// Represents one external IP reputation evaluation.
    /// </summary>
    public sealed class BlackListReputationResult
    {
        /// <summary>
        /// Gets whether the provider completed the evaluation successfully.
        /// </summary>
        public bool Succeeded { get; private set; }

        /// <summary>
        /// Gets whether the provider considers the IP address blocked.
        /// </summary>
        public bool IsListed { get; private set; }

        /// <summary>
        /// Gets the human-readable evaluation details.
        /// </summary>
        public string Details { get; private set; }

        /// <summary>
        /// Creates a successful reputation result.
        /// </summary>
        /// <param name="isListed">Whether the address is listed.</param>
        /// <param name="details">Evaluation details.</param>
        /// <returns>A successful result.</returns>
        public static BlackListReputationResult Success(bool isListed, string details = null)
        {
            // Keep construction centralized so custom providers return a consistent contract.
            return new BlackListReputationResult
            {
                Succeeded = true,
                IsListed = isListed,
                Details = details
            };
        }

        /// <summary>
        /// Creates a failed reputation result that does not block the address.
        /// </summary>
        /// <param name="details">Failure details.</param>
        /// <returns>A failed fail-open result.</returns>
        public static BlackListReputationResult Failure(string details)
        {
            // Reputation provider failures must never turn into implicit bans.
            return new BlackListReputationResult
            {
                Succeeded = false,
                IsListed = false,
                Details = details
            };
        }
    }
}
