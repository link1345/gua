#define WIN32_LEAN_AND_MEAN

#include "gua/ws_bridge.hpp"

#include <winsock2.h>
#include <ws2tcpip.h>

#include <algorithm>
#include <array>
#include <atomic>
#include <cstdint>
#include <cstring>
#include <future>
#include <iostream>
#include <mutex>
#include <optional>
#include <stdexcept>
#include <string>
#include <thread>
#include <vector>

namespace {

constexpr std::string_view websocket_guid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

struct Command {
    int id = 0;
    std::string type;
    std::string node_id;
    std::string key;
};

class Socket {
public:
    explicit Socket(SOCKET value = INVALID_SOCKET)
        : value_(value)
    {
    }

    Socket(const Socket&) = delete;
    Socket& operator=(const Socket&) = delete;

    Socket(Socket&& other) noexcept
        : value_(other.value_)
    {
        other.value_ = INVALID_SOCKET;
    }

    Socket& operator=(Socket&& other) noexcept
    {
        if (this != &other) {
            close();
            value_ = other.value_;
            other.value_ = INVALID_SOCKET;
        }
        return *this;
    }

    ~Socket()
    {
        close();
    }

    [[nodiscard]] SOCKET get() const noexcept
    {
        return value_;
    }

    [[nodiscard]] bool valid() const noexcept
    {
        return value_ != INVALID_SOCKET;
    }

    SOCKET release() noexcept
    {
        const SOCKET value = value_;
        value_ = INVALID_SOCKET;
        return value;
    }

    void close() noexcept
    {
        if (value_ != INVALID_SOCKET) {
            closesocket(value_);
            value_ = INVALID_SOCKET;
        }
    }

private:
    SOCKET value_;
};

class WinsockSession {
public:
    WinsockSession()
    {
        WSADATA data {};
        if (WSAStartup(MAKEWORD(2, 2), &data) != 0) {
            throw std::runtime_error("WSAStartup failed");
        }
    }

    WinsockSession(const WinsockSession&) = delete;
    WinsockSession& operator=(const WinsockSession&) = delete;

    ~WinsockSession()
    {
        WSACleanup();
    }
};

std::string escape_json(std::string_view value)
{
    std::string out;
    out.reserve(value.size());
    for (const char ch : value) {
        switch (ch) {
        case '"':
            out += "\\\"";
            break;
        case '\\':
            out += "\\\\";
            break;
        case '\n':
            out += "\\n";
            break;
        case '\r':
            out += "\\r";
            break;
        case '\t':
            out += "\\t";
            break;
        default:
            out += ch;
            break;
        }
    }
    return out;
}

std::uint32_t rotate_left(std::uint32_t value, int bits)
{
    return (value << bits) | (value >> (32 - bits));
}

std::array<std::uint8_t, 20> sha1(std::string_view input)
{
    std::vector<std::uint8_t> message(input.begin(), input.end());
    const std::uint64_t bit_length = static_cast<std::uint64_t>(message.size()) * 8U;

    message.push_back(0x80U);
    while ((message.size() % 64U) != 56U) {
        message.push_back(0U);
    }
    for (int shift = 56; shift >= 0; shift -= 8) {
        message.push_back(static_cast<std::uint8_t>((bit_length >> shift) & 0xffU));
    }

    std::uint32_t h0 = 0x67452301U;
    std::uint32_t h1 = 0xefcdab89U;
    std::uint32_t h2 = 0x98badcfeU;
    std::uint32_t h3 = 0x10325476U;
    std::uint32_t h4 = 0xc3d2e1f0U;

    for (std::size_t chunk = 0; chunk < message.size(); chunk += 64U) {
        std::array<std::uint32_t, 80> words {};
        for (std::size_t i = 0; i < 16U; ++i) {
            const std::size_t offset = chunk + (i * 4U);
            words[i] = (static_cast<std::uint32_t>(message[offset]) << 24U)
                | (static_cast<std::uint32_t>(message[offset + 1U]) << 16U)
                | (static_cast<std::uint32_t>(message[offset + 2U]) << 8U)
                | static_cast<std::uint32_t>(message[offset + 3U]);
        }
        for (std::size_t i = 16U; i < 80U; ++i) {
            words[i] = rotate_left(words[i - 3U] ^ words[i - 8U] ^ words[i - 14U] ^ words[i - 16U], 1);
        }

        std::uint32_t a = h0;
        std::uint32_t b = h1;
        std::uint32_t c = h2;
        std::uint32_t d = h3;
        std::uint32_t e = h4;

        for (std::size_t i = 0; i < 80U; ++i) {
            std::uint32_t f = 0;
            std::uint32_t k = 0;
            if (i < 20U) {
                f = (b & c) | ((~b) & d);
                k = 0x5a827999U;
            } else if (i < 40U) {
                f = b ^ c ^ d;
                k = 0x6ed9eba1U;
            } else if (i < 60U) {
                f = (b & c) | (b & d) | (c & d);
                k = 0x8f1bbcdcU;
            } else {
                f = b ^ c ^ d;
                k = 0xca62c1d6U;
            }
            const std::uint32_t temp = rotate_left(a, 5) + f + e + k + words[i];
            e = d;
            d = c;
            c = rotate_left(b, 30);
            b = a;
            a = temp;
        }

        h0 += a;
        h1 += b;
        h2 += c;
        h3 += d;
        h4 += e;
    }

    const std::array<std::uint32_t, 5> words { h0, h1, h2, h3, h4 };
    std::array<std::uint8_t, 20> digest {};
    for (std::size_t i = 0; i < words.size(); ++i) {
        digest[(i * 4U) + 0U] = static_cast<std::uint8_t>((words[i] >> 24U) & 0xffU);
        digest[(i * 4U) + 1U] = static_cast<std::uint8_t>((words[i] >> 16U) & 0xffU);
        digest[(i * 4U) + 2U] = static_cast<std::uint8_t>((words[i] >> 8U) & 0xffU);
        digest[(i * 4U) + 3U] = static_cast<std::uint8_t>(words[i] & 0xffU);
    }
    return digest;
}

std::string base64_encode(const std::uint8_t* data, std::size_t size)
{
    constexpr char alphabet[] = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    std::string out;
    out.reserve(((size + 2U) / 3U) * 4U);

    for (std::size_t i = 0; i < size; i += 3U) {
        const std::uint32_t a = data[i];
        const std::uint32_t b = i + 1U < size ? data[i + 1U] : 0U;
        const std::uint32_t c = i + 2U < size ? data[i + 2U] : 0U;
        const std::uint32_t triple = (a << 16U) | (b << 8U) | c;
        out.push_back(alphabet[(triple >> 18U) & 0x3fU]);
        out.push_back(alphabet[(triple >> 12U) & 0x3fU]);
        out.push_back(i + 1U < size ? alphabet[(triple >> 6U) & 0x3fU] : '=');
        out.push_back(i + 2U < size ? alphabet[triple & 0x3fU] : '=');
    }
    return out;
}

std::string websocket_accept_key(std::string_view key)
{
    std::string combined(key);
    combined += websocket_guid;
    const auto digest = sha1(combined);
    return base64_encode(digest.data(), digest.size());
}

void send_all(SOCKET socket, const std::uint8_t* data, std::size_t size)
{
    std::size_t sent = 0;
    while (sent < size) {
        const int result = send(socket, reinterpret_cast<const char*>(data + sent), static_cast<int>(size - sent), 0);
        if (result <= 0) {
            throw std::runtime_error("send failed");
        }
        sent += static_cast<std::size_t>(result);
    }
}

void send_all(SOCKET socket, std::string_view text)
{
    send_all(socket, reinterpret_cast<const std::uint8_t*>(text.data()), text.size());
}

std::vector<std::uint8_t> recv_exact(SOCKET socket, std::size_t size)
{
    std::vector<std::uint8_t> data(size);
    std::size_t received = 0;
    while (received < size) {
        const int result = recv(socket, reinterpret_cast<char*>(data.data() + received), static_cast<int>(size - received), 0);
        if (result <= 0) {
            throw std::runtime_error("connection closed");
        }
        received += static_cast<std::size_t>(result);
    }
    return data;
}

std::string read_http_headers(SOCKET socket)
{
    std::string headers;
    std::array<char, 1024> buffer {};
    while (headers.find("\r\n\r\n") == std::string::npos) {
        const int result = recv(socket, buffer.data(), static_cast<int>(buffer.size()), 0);
        if (result <= 0) {
            throw std::runtime_error("failed to read handshake");
        }
        headers.append(buffer.data(), static_cast<std::size_t>(result));
        if (headers.size() > 16384U) {
            throw std::runtime_error("handshake headers are too large");
        }
    }
    return headers;
}

std::optional<std::string> header_value(std::string_view headers, std::string_view name)
{
    std::size_t start = 0;
    while (start < headers.size()) {
        const std::size_t end = headers.find("\r\n", start);
        if (end == std::string_view::npos) {
            break;
        }
        const std::string_view line = headers.substr(start, end - start);
        const std::size_t colon = line.find(':');
        if (colon != std::string_view::npos) {
            const std::string_view candidate = line.substr(0, colon);
            if (_strnicmp(candidate.data(), name.data(), name.size()) == 0 && candidate.size() == name.size()) {
                std::size_t value_start = colon + 1U;
                while (value_start < line.size() && line[value_start] == ' ') {
                    ++value_start;
                }
                return std::string(line.substr(value_start));
            }
        }
        start = end + 2U;
    }
    return std::nullopt;
}

void perform_handshake(SOCKET socket)
{
    const std::string headers = read_http_headers(socket);
    const std::optional<std::string> key = header_value(headers, "Sec-WebSocket-Key");
    if (!key.has_value()) {
        throw std::runtime_error("missing Sec-WebSocket-Key");
    }

    const std::string response = "HTTP/1.1 101 Switching Protocols\r\n"
        "Upgrade: websocket\r\n"
        "Connection: Upgrade\r\n"
        "Sec-WebSocket-Accept: "
        + websocket_accept_key(*key)
        + "\r\n\r\n";
    send_all(socket, response);
}

std::optional<std::string> read_text_frame(SOCKET socket)
{
    const auto header = recv_exact(socket, 2);
    const std::uint8_t opcode = header[0] & 0x0fU;
    const bool masked = (header[1] & 0x80U) != 0U;
    std::uint64_t length = header[1] & 0x7fU;

    if (opcode == 0x8U) {
        return std::nullopt;
    }
    if (opcode != 0x1U) {
        throw std::runtime_error("only text WebSocket frames are supported");
    }
    if (!masked) {
        throw std::runtime_error("client WebSocket frames must be masked");
    }
    if (length == 126U) {
        const auto bytes = recv_exact(socket, 2);
        length = (static_cast<std::uint64_t>(bytes[0]) << 8U) | bytes[1];
    } else if (length == 127U) {
        const auto bytes = recv_exact(socket, 8);
        length = 0;
        for (const std::uint8_t byte : bytes) {
            length = (length << 8U) | byte;
        }
    }
    if (length > 1024U * 1024U) {
        throw std::runtime_error("WebSocket frame is too large");
    }

    const auto mask = recv_exact(socket, 4);
    auto payload = recv_exact(socket, static_cast<std::size_t>(length));
    for (std::size_t i = 0; i < payload.size(); ++i) {
        payload[i] ^= mask[i % 4U];
    }
    return std::string(payload.begin(), payload.end());
}

void send_text_frame(SOCKET socket, std::string_view text)
{
    std::vector<std::uint8_t> frame;
    frame.push_back(0x81U);
    if (text.size() <= 125U) {
        frame.push_back(static_cast<std::uint8_t>(text.size()));
    } else if (text.size() <= 65535U) {
        frame.push_back(126U);
        frame.push_back(static_cast<std::uint8_t>((text.size() >> 8U) & 0xffU));
        frame.push_back(static_cast<std::uint8_t>(text.size() & 0xffU));
    } else {
        frame.push_back(127U);
        const std::uint64_t size = static_cast<std::uint64_t>(text.size());
        for (int shift = 56; shift >= 0; shift -= 8) {
            frame.push_back(static_cast<std::uint8_t>((size >> shift) & 0xffU));
        }
    }
    frame.insert(frame.end(), text.begin(), text.end());
    send_all(socket, frame.data(), frame.size());
}

std::optional<std::string> json_string_field(std::string_view json, std::string_view field)
{
    const std::string key = "\"" + std::string(field) + "\"";
    const std::size_t key_position = json.find(key);
    if (key_position == std::string_view::npos) {
        return std::nullopt;
    }
    const std::size_t colon = json.find(':', key_position + key.size());
    if (colon == std::string_view::npos) {
        return std::nullopt;
    }
    const std::size_t quote = json.find('"', colon + 1U);
    if (quote == std::string_view::npos) {
        return std::nullopt;
    }

    std::string value;
    for (std::size_t i = quote + 1U; i < json.size(); ++i) {
        const char ch = json[i];
        if (ch == '\\' && i + 1U < json.size()) {
            value.push_back(json[++i]);
            continue;
        }
        if (ch == '"') {
            return value;
        }
        value.push_back(ch);
    }
    return std::nullopt;
}

std::optional<int> json_int_field(std::string_view json, std::string_view field)
{
    const std::string key = "\"" + std::string(field) + "\"";
    const std::size_t key_position = json.find(key);
    if (key_position == std::string_view::npos) {
        return std::nullopt;
    }
    const std::size_t colon = json.find(':', key_position + key.size());
    if (colon == std::string_view::npos) {
        return std::nullopt;
    }

    std::size_t start = colon + 1U;
    while (start < json.size() && json[start] == ' ') {
        ++start;
    }
    std::size_t end = start;
    while (end < json.size() && json[end] >= '0' && json[end] <= '9') {
        ++end;
    }
    if (end == start) {
        return std::nullopt;
    }
    return std::stoi(std::string(json.substr(start, end - start)));
}

Command parse_command(std::string_view json)
{
    Command command;
    command.id = json_int_field(json, "id").value_or(0);
    command.type = json_string_field(json, "type").value_or("");
    command.node_id = json_string_field(json, "nodeId").value_or("");
    command.key = json_string_field(json, "key").value_or("");
    return command;
}

std::string ok_response(int id, std::string_view result_json)
{
    return "{\"id\":" + std::to_string(id) + ",\"ok\":true,\"result\":" + std::string(result_json) + "}";
}

std::string ok_null_response(int id)
{
    return "{\"id\":" + std::to_string(id) + ",\"ok\":true,\"result\":null}";
}

std::string error_response(int id, std::string_view message)
{
    return "{\"id\":" + std::to_string(id) + ",\"ok\":false,\"error\":\"" + escape_json(message) + "\"}";
}

Socket create_listen_socket(unsigned short port)
{
    Socket listen_socket(socket(AF_INET, SOCK_STREAM, IPPROTO_TCP));
    if (!listen_socket.valid()) {
        throw std::runtime_error("socket failed");
    }

    int reuse = 1;
    setsockopt(listen_socket.get(), SOL_SOCKET, SO_REUSEADDR, reinterpret_cast<const char*>(&reuse), sizeof(reuse));

    sockaddr_in address {};
    address.sin_family = AF_INET;
    address.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
    address.sin_port = htons(port);

    if (bind(listen_socket.get(), reinterpret_cast<sockaddr*>(&address), sizeof(address)) == SOCKET_ERROR) {
        throw std::runtime_error("bind failed");
    }
    if (listen(listen_socket.get(), SOMAXCONN) == SOCKET_ERROR) {
        throw std::runtime_error("listen failed");
    }

    return listen_socket;
}

} // namespace

namespace gua::ws {

class BridgeServer::Impl {
public:
    Impl(BridgeHandlers handlers, BridgeOptions options)
        : handlers_(std::move(handlers))
        , options_(options)
    {
    }

    ~Impl()
    {
        stop();
    }

    void start()
    {
        if (running_.exchange(true)) {
            return;
        }

        std::promise<bool> started;
        std::future<bool> started_future = started.get_future();
        thread_ = std::thread([this, started = std::move(started)]() mutable {
            run(std::move(started));
        });

        if (!started_future.get()) {
            if (thread_.joinable()) {
                thread_.join();
            }
        }
    }

    void stop()
    {
        if (!running_.exchange(false)) {
            return;
        }
        if (listen_socket_ != INVALID_SOCKET) {
            closesocket(listen_socket_);
            listen_socket_ = INVALID_SOCKET;
        }
        {
            const std::lock_guard lock(clients_mutex_);
            for (const SOCKET client : clients_) {
                shutdown(client, SD_BOTH);
            }
        }
        if (thread_.joinable()) {
            thread_.join();
        }
    }

    void publish_snapshot()
    {
        if (!running_.load()) {
            return;
        }
        {
            const std::lock_guard lock(clients_mutex_);
            if (clients_.empty()) {
                return;
            }
        }

        std::string message;
        try {
            message = "{\"type\":\"snapshot\",\"snapshot\":{\"uiTree\":"
                + handlers_.get_ui_tree_json()
                + ",\"logs\":"
                + handlers_.get_logs_json()
                + ",\"screenshot\":"
                + handlers_.get_screenshot_json()
                + "}}";
        } catch (const std::exception& error) {
            std::cerr << "Gua bridge snapshot failed: " << error.what() << std::endl;
            return;
        }

        const std::lock_guard lock(clients_mutex_);
        auto write = clients_.begin();
        for (auto read = clients_.begin(); read != clients_.end(); ++read) {
            try {
                send_text_frame(*read, message);
                *write++ = *read;
            } catch (...) {
                closesocket(*read);
            }
        }
        clients_.erase(write, clients_.end());
    }

    [[nodiscard]] bool running() const
    {
        return running_.load();
    }

    [[nodiscard]] unsigned short port() const
    {
        return options_.port;
    }

private:
    void run(std::promise<bool> started)
    {
        bool startup_reported = false;
        try {
            const WinsockSession winsock;
            Socket listen_socket = create_listen_socket(options_.port);
            listen_socket_ = listen_socket.get();
            started.set_value(true);
            startup_reported = true;
            std::cout << "Gua WebSocket bridge listening on ws://127.0.0.1:" << options_.port << std::endl;

            while (running_.load()) {
                Socket client(accept(listen_socket.get(), nullptr, nullptr));
                if (!client.valid()) {
                    if (running_.load()) {
                        continue;
                    }
                    break;
                }

                try {
                    serve_client(client.get());
                } catch (const std::exception& error) {
                    if (running_.load()) {
                        std::cerr << "Gua bridge client error: " << error.what() << std::endl;
                    }
                }
            }

            listen_socket_ = INVALID_SOCKET;
        } catch (const std::exception& error) {
            std::cerr << "Gua bridge failed: " << error.what() << std::endl;
            running_.store(false);
            if (!startup_reported) {
                started.set_value(false);
            }
        }
    }

    void serve_client(SOCKET client)
    {
        perform_handshake(client);
        {
            const std::lock_guard lock(clients_mutex_);
            clients_.push_back(client);
        }
        std::cout << "Inspector connected." << std::endl;
        publish_snapshot();

        while (running_.load()) {
            const std::optional<std::string> message = read_text_frame(client);
            if (!message.has_value()) {
                break;
            }

            const std::string response = handle_command(*message);
            send_text_frame(client, response);
        }

        {
            const std::lock_guard lock(clients_mutex_);
            clients_.erase(std::remove(clients_.begin(), clients_.end(), client), clients_.end());
        }
        std::cout << "Inspector disconnected." << std::endl;
    }

    [[nodiscard]] std::string handle_command(std::string_view message)
    {
        const Command command = parse_command(message);
        try {
            if (command.type == "get_ui_tree") {
                return ok_response(command.id, handlers_.get_ui_tree_json());
            }
            if (command.type == "get_logs") {
                return ok_response(command.id, handlers_.get_logs_json());
            }
            if (command.type == "get_screenshot") {
                return ok_response(command.id, handlers_.get_screenshot_json());
            }
            if (command.type == "click_node") {
                return handlers_.click_node(command.node_id)
                    ? ok_null_response(command.id)
                    : error_response(command.id, "Gua node not found or not clickable: " + command.node_id);
            }
            if (command.type == "focus_node") {
                return handlers_.focus_node(command.node_id)
                    ? ok_null_response(command.id)
                    : error_response(command.id, "Gua node not found: " + command.node_id);
            }
            if (command.type == "press_key") {
                if (!handlers_.press_key) {
                    return error_response(command.id, "press_key is not supported by this bridge");
                }

                return handlers_.press_key(command.key)
                    ? ok_null_response(command.id)
                    : error_response(command.id, "Gua key was not accepted: " + command.key);
            }
            return error_response(command.id, "Unsupported command: " + command.type);
        } catch (const std::exception& error) {
            return error_response(command.id, error.what());
        }
    }

    BridgeHandlers handlers_;
    BridgeOptions options_;
    std::atomic_bool running_ = false;
    std::thread thread_;
    std::atomic<SOCKET> listen_socket_ = INVALID_SOCKET;
    std::mutex clients_mutex_;
    std::vector<SOCKET> clients_;
};

BridgeServer::BridgeServer(BridgeHandlers handlers, BridgeOptions options)
    : impl_(std::make_unique<Impl>(std::move(handlers), options))
{
}

BridgeServer::BridgeServer(BridgeServer&&) noexcept = default;

BridgeServer& BridgeServer::operator=(BridgeServer&&) noexcept = default;

BridgeServer::~BridgeServer() = default;

void BridgeServer::start()
{
    impl_->start();
}

void BridgeServer::stop()
{
    impl_->stop();
}

void BridgeServer::publish_snapshot()
{
    impl_->publish_snapshot();
}

bool BridgeServer::running() const
{
    return impl_->running();
}

unsigned short BridgeServer::port() const
{
    return impl_->port();
}

} // namespace gua::ws
