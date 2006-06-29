//
// socket_types.hpp
// ~~~~~~~~~~~~~~~~
//
// Copyright (c) 2003-2005 Christopher M. Kohlhoff (chris at kohlhoff dot com)
//
// Distributed under the Boost Software License, Version 1.0. (See accompanying
// file LICENSE_1_0.txt or copy at http://www.boost.org/LICENSE_1_0.txt)
//

#ifndef BOOST_ASIO_DETAIL_SOCKET_TYPES_HPP
#define BOOST_ASIO_DETAIL_SOCKET_TYPES_HPP

#if defined(_MSC_VER) && (_MSC_VER >= 1200)
# pragma once
#endif // defined(_MSC_VER) && (_MSC_VER >= 1200)

#include <boost/asio/detail/push_options.hpp>

#include <boost/asio/detail/push_options.hpp>
#include <boost/config.hpp>
#include <boost/asio/detail/pop_options.hpp>

#include <boost/asio/detail/push_options.hpp>
#if defined(BOOST_WINDOWS)
# if !defined(_WIN32_WINNT) && !defined(_WIN32_WINDOWS)
#  if defined(_MSC_VER) || defined(__BORLANDC__)
#   pragma message("Please define _WIN32_WINNT or _WIN32_WINDOWS appropriately")
#   pragma message("Assuming _WIN32_WINNT=0x0500 (i.e. Windows 2000 target)")
#  else // defined(_MSC_VER) || defined(__BORLANDC__)
#   warning Please define _WIN32_WINNT or _WIN32_WINDOWS appropriately
#   warning Assuming _WIN32_WINNT=0x0500 (i.e. Windows 2000 target)
#  endif // defined(_MSC_VER) || defined(__BORLANDC__)
#  define _WIN32_WINNT 0x0500
# endif // !defined(_WIN32_WINNT) && !defined(_WIN32_WINDOWS)
# if defined(__BORLANDC__) && !defined(_WSPIAPI_H_)
#  include <stdlib.h> // Needed for __errno
#  define _WSPIAPI_H_
#  define BOOST_ASIO_WSPIAPI_H_DEFINED
# endif // defined(__BORLANDC__) && !defined(_WSPIAPI_H_)
# define FD_SETSIZE 1024
# include <winsock2.h>
# include <ws2tcpip.h>
# include <mswsock.h>
# if defined(BOOST_ASIO_WSPIAPI_H_DEFINED)
#  undef _WSPIAPI_H_
#  undef BOOST_ASIO_WSPIAPI_H_DEFINED
# endif // defined(BOOST_ASIO_WSPIAPI_H_DEFINED)
# if defined(_MSC_VER) || defined(__BORLANDC__)
#  pragma comment(lib, "ws2_32.lib")
#  pragma comment(lib, "mswsock.lib")
# endif // defined(_MSC_VER) || defined(__BORLANDC__)
#else
# include <sys/ioctl.h>
# include <sys/types.h>
# include <sys/select.h>
# include <sys/socket.h>
# include <sys/uio.h>
# include <netinet/in.h>
# include <netinet/tcp.h>
# include <arpa/inet.h>
# include <netdb.h>
# if defined(__sun)
#  include <sys/filio.h>
# endif
#endif
#include <boost/asio/detail/pop_options.hpp>

namespace boost {
namespace asio {
namespace detail {

#if defined(BOOST_WINDOWS)
typedef SOCKET socket_type;
const SOCKET invalid_socket = INVALID_SOCKET;
const int socket_error_retval = SOCKET_ERROR;
const int max_addr_str_len = 256;
typedef sockaddr socket_addr_type;
typedef sockaddr_in inet_addr_v4_type;
typedef int socket_addr_len_type;
typedef unsigned long ioctl_arg_type;
typedef u_long u_long_type;
typedef u_short u_short_type;
const int shutdown_receive = SD_RECEIVE;
const int shutdown_send = SD_SEND;
const int shutdown_both = SD_BOTH;
const int message_peek = MSG_PEEK;
const int message_out_of_band = MSG_OOB;
const int message_do_not_route = MSG_DONTROUTE;
#else
typedef int socket_type;
const int invalid_socket = -1;
const int socket_error_retval = -1;
const int max_addr_str_len = INET_ADDRSTRLEN;
typedef sockaddr socket_addr_type;
typedef sockaddr_in inet_addr_v4_type;
typedef socklen_t socket_addr_len_type;
typedef int ioctl_arg_type;
typedef uint32_t u_long_type;
typedef uint16_t u_short_type;
const int shutdown_receive = SHUT_RD;
const int shutdown_send = SHUT_WR;
const int shutdown_both = SHUT_RDWR;
const int message_peek = MSG_PEEK;
const int message_out_of_band = MSG_OOB;
const int message_do_not_route = MSG_DONTROUTE;
#endif

} // namespace detail
} // namespace asio
} // namespace boost

#include <boost/asio/detail/pop_options.hpp>

#endif // BOOST_ASIO_DETAIL_SOCKET_TYPES_HPP