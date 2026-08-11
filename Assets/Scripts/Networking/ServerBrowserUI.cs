using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// Server browser: detected LAN games as clickable rows, plus manual ip/port entry.
///
/// Manual entry is not just a fallback - broadcast discovery is unreliable across a virtual
/// LAN such as Hamachi, and after a host migration the host is on an ephemeral port that only
/// discovery (or typing it) can reveal.
/// </summary>
public class ServerBrowserUI : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject browserPanel;
    [SerializeField] private GameObject connectionPanel;

    [Header("List")]
    [SerializeField] private Transform listRoot;
    [SerializeField] private GameObject rowTemplate;
    [SerializeField] private TMP_Text emptyLabel;

    [Header("Manual entry")]
    [SerializeField] private TMP_InputField ipField;
    [SerializeField] private TMP_InputField portField;

    private readonly List<GameObject> rows = new List<GameObject>();
    private readonly List<DiscoveredHost> shown = new List<DiscoveredHost>();
    private float nextRefresh;

    // This component lives on the panel it controls, so Start does not run until the panel is
    // first activated - i.e. inside the very Open() call that activated it. Hiding the panel
    // here would slam it shut again, which is why the first click appeared to do nothing.
    // The panel's initial hidden state is set in the scene instead.
    private void Awake()
    {
        if (rowTemplate != null)
            rowTemplate.SetActive(false);
    }

    public void Open()
    {
        if (browserPanel != null) browserPanel.SetActive(true);
        if (connectionPanel != null) connectionPanel.SetActive(false);

        Refresh();
    }

    public void Close()
    {
        if (browserPanel != null) browserPanel.SetActive(false);
        if (connectionPanel != null) connectionPanel.SetActive(true);
    }

    public void Refresh()
    {
        if (LanDiscovery.Instance != null)
            LanDiscovery.Instance.Refresh();
    }

    private void Update()
    {
        if (browserPanel == null || !browserPanel.activeSelf)
            return;

        if (Time.time < nextRefresh)
            return;

        nextRefresh = Time.time + 1f;

        Refresh();
        Rebuild();
    }

    private void Rebuild()
    {
        if (listRoot == null || rowTemplate == null || LanDiscovery.Instance == null)
            return;

        shown.Clear();
        foreach (var host in LanDiscovery.Instance.Hosts)
            shown.Add(host);

        while (rows.Count < shown.Count)
        {
            GameObject row = Instantiate(rowTemplate, listRoot);
            rows.Add(row);
        }

        for (int i = 0; i < rows.Count; i++)
        {
            bool used = i < shown.Count;
            rows[i].SetActive(used);

            if (!used)
                continue;

            DiscoveredHost host = shown[i];

            TMP_Text label = rows[i].GetComponentInChildren<TMP_Text>(true);
            if (label != null)
                label.text = host.IsFull
                    ? $"{host.Endpoint}   {host.players}/{host.maxPlayers}  FULL"
                    : $"{host.Endpoint}   {host.players}/{host.maxPlayers}";

            Button button = rows[i].GetComponent<Button>();
            if (button != null)
            {
                button.interactable = !host.IsFull;

                // Rebound every rebuild: the row's index-to-host mapping changes as hosts
                // appear and disappear, so a stale listener would join the wrong game.
                button.onClick.RemoveAllListeners();
                DiscoveredHost captured = host;
                button.onClick.AddListener(() => JoinEndpoint(captured.address, captured.port));
            }
        }

        if (emptyLabel != null)
            emptyLabel.gameObject.SetActive(shown.Count == 0);
    }

    /// <summary>Join using whatever is typed in the ip and port fields.</summary>
    public void JoinManual()
    {
        string address = ipField != null && !string.IsNullOrWhiteSpace(ipField.text)
            ? ipField.text.Trim()
            : "127.0.0.1";

        int port = 7777;

        if (portField != null && !string.IsNullOrWhiteSpace(portField.text))
        {
            if (!int.TryParse(portField.text.Trim(), out port) || port <= 0 || port > 65535)
            {
                Debug.LogWarning($"[ServerBrowser] '{portField.text}' is not a valid port");
                return;
            }
        }

        JoinEndpoint(address, port);
    }

    private void JoinEndpoint(string address, int port)
    {
        if (NetworkManager.Instance == null)
            return;

        Debug.Log($"[ServerBrowser] Dialling {address}:{port}");

        if (NetworkManager.Instance.JoinLobby(address, port))
        {
            if (browserPanel != null)
                browserPanel.SetActive(false);
        }
    }
}
