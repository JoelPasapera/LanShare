namespace LanShare.Net
{
    /// <summary>
    /// Como clasifica Windows una red, con los mismos valores que
    /// NLM_NETWORK_CATEGORY. El firewall aplica sus reglas segun esto: las de
    /// LanShare solo abren las redes privadas y las de dominio.
    /// </summary>
    public enum NetworkCategory
    {
        Public = 0,
        Private = 1,
        Domain = 2
    }
}
