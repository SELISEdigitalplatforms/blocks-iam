namespace Authentication.DomainService.Shared.RequestModel
{
    public class UpdateStatusRequest
    {
        /// <summary>
        /// Whether the resource is active or inactive
        /// </summary>
        public bool IsActive { get; set; }
    }
}
