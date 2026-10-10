param([string]$BaseUrl = 'http://localhost:5188')
$ErrorActionPreference = 'Stop'
# This script modifies only local demo data: it reserves one unit, then cancels.
$uri = [Uri]$BaseUrl
if (-not $uri.IsLoopback) { throw 'This demo script only accepts a loopback URL.' }
$login = Invoke-WebRequest "$BaseUrl/Account/Login" -SessionVariable demoSession
$match = [regex]::Match($login.Content, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"')
if (-not $match.Success) { throw 'Could not read the login CSRF token.' }
$token = [Net.WebUtility]::HtmlDecode($match.Groups[1].Value)
Invoke-WebRequest "$BaseUrl/Account/Login" -Method Post -WebSession $demoSession -Body @{
    Email = 'admin@stockpilot.demo'
    Password = 'StockPilot!2026'
    __RequestVerificationToken = $token
} | Out-Null
$csrf = Invoke-RestMethod "$BaseUrl/api/csrf" -WebSession $demoSession
$headers = @{ 'X-CSRF-TOKEN' = $csrf.token }
$products = Invoke-RestMethod "$BaseUrl/api/products" -WebSession $demoSession
$product = $products | Where-Object { $_.available -gt 0 } | Select-Object -First 1
if (-not $product) { throw 'Receive stock in the demo before running this script.' }
$payload = @{ productId = $product.id; quantity = 1; customer = 'API demo'; requestId = [Guid]::NewGuid().ToString() } | ConvertTo-Json
$order = Invoke-RestMethod "$BaseUrl/api/orders" -Method Post -WebSession $demoSession -Headers $headers -ContentType 'application/json' -Body $payload
Write-Output "Reserved order $($order.id) for one $($product.name)."
Invoke-RestMethod "$BaseUrl/api/orders/$($order.id)/cancel" -Method Post -WebSession $demoSession -Headers $headers | Out-Null
Write-Output 'Cancelled the demo order; availability restored. Review Activity for the audit trail.'
