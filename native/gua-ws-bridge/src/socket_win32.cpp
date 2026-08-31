#define WIN32_LEAN_AND_MEAN

#include "socket_platform.hpp"

#include <winsock2.h>
#include <ws2tcpip.h>

#include <algorithm>
#include <climits>
#include <stdexcept>

namespace gua::ws::platform {
namespace {

SOCKET native(SocketHandle socket) noexcept
{
    return static_cast<SOCKET>(socket);
}

SocketHandle portable(SOCKET socket) noexcept
{
    return socket == INVALID_SOCKET ? invalid_socket : static_cast<SocketHandle>(socket);
}

} // namespace

NetworkSession::NetworkSession()
{
    WSADATA data {};
    if (WSAStartup(MAKEWORD(2, 2), &data) != 0) {
        throw std::runtime_error("WSAStartup failed");
    }
}

NetworkSession::~NetworkSession()
{
    WSACleanup();
}

Socket::Socket(SocketHandle value) noexcept : value_(value) {}
Socket::Socket(Socket&& other) noexcept : value_(other.release()) {}
Socket& Socket::operator=(Socket&& other) noexcept
{
    if (this != &other) {
        close();
        value_ = other.release();
    }
    return *this;
}
Socket::~Socket() { close(); }
SocketHandle Socket::get() const noexcept { return value_; }
bool Socket::valid() const noexcept { return value_ != invalid_socket; }
SocketHandle Socket::release() noexcept
{
    const SocketHandle value = value_;
    value_ = invalid_socket;
    return value;
}
void Socket::close() noexcept
{
    if (valid()) {
        close_socket(value_);
        value_ = invalid_socket;
    }
}

Socket create_listen_socket(unsigned short port)
{
    Socket listen_socket(portable(::socket(AF_INET, SOCK_STREAM, IPPROTO_TCP)));
    if (!listen_socket.valid()) throw std::runtime_error("socket failed");

    int reuse = 1;
    if (::setsockopt(native(listen_socket.get()), SOL_SOCKET, SO_REUSEADDR,
            reinterpret_cast<const char*>(&reuse), sizeof(reuse)) == SOCKET_ERROR)
        throw std::runtime_error("setsockopt failed");

    sockaddr_in address {};
    address.sin_family = AF_INET;
    address.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
    address.sin_port = htons(port);
    if (::bind(native(listen_socket.get()), reinterpret_cast<sockaddr*>(&address), sizeof(address)) == SOCKET_ERROR)
        throw std::runtime_error("bind failed");
    if (::listen(native(listen_socket.get()), SOMAXCONN) == SOCKET_ERROR)
        throw std::runtime_error("listen failed");
    return listen_socket;
}

unsigned short bound_port(SocketHandle listen_socket)
{
    sockaddr_in address {};
    int address_size = sizeof(address);
    if (::getsockname(native(listen_socket), reinterpret_cast<sockaddr*>(&address), &address_size) == SOCKET_ERROR ||
        address_size < static_cast<int>(sizeof(address)) || address.sin_family != AF_INET)
        throw std::runtime_error("getsockname failed");
    return ntohs(address.sin_port);
}

Socket accept_socket(SocketHandle listen_socket) noexcept
{
    return Socket(portable(::accept(native(listen_socket), nullptr, nullptr)));
}

bool wake_listener(unsigned short port) noexcept
{
    Socket wake(portable(::socket(AF_INET, SOCK_STREAM, IPPROTO_TCP)));
    if (!wake.valid()) return false;
    sockaddr_in address {};
    address.sin_family = AF_INET;
    address.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
    address.sin_port = htons(port);
    return ::connect(native(wake.get()), reinterpret_cast<sockaddr*>(&address), sizeof(address)) != SOCKET_ERROR;
}

std::ptrdiff_t send_some(SocketHandle socket, const std::uint8_t* data, std::size_t size) noexcept
{
    const int count = static_cast<int>((std::min)(size, static_cast<std::size_t>(INT_MAX)));
    return ::send(native(socket), reinterpret_cast<const char*>(data), count, 0);
}

std::ptrdiff_t receive_some(SocketHandle socket, std::uint8_t* data, std::size_t size) noexcept
{
    const int count = static_cast<int>((std::min)(size, static_cast<std::size_t>(INT_MAX)));
    return ::recv(native(socket), reinterpret_cast<char*>(data), count, 0);
}

void shutdown_socket(SocketHandle socket) noexcept
{
    if (socket != invalid_socket) (void)::shutdown(native(socket), SD_BOTH);
}

void close_socket(SocketHandle socket) noexcept
{
    if (socket != invalid_socket) (void)::closesocket(native(socket));
}

} // namespace gua::ws::platform
